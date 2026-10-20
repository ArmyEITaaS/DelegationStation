using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using CorporateIdentifierSync.Interfaces;
using DelegationStationShared;
using DelegationStationShared.Extensions;
using Microsoft.Extensions.Logging;

namespace CorporateIdentifierSync.Services
{
    public class BlobLeaseSingletonLock : IFunctionSingletonLock
    {
        private const string ContainerName = "function-locks";
        private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan RenewInterval = TimeSpan.FromSeconds(30);

        private readonly ILogger<BlobLeaseSingletonLock> _logger;
        private readonly BlobContainerClient _container;

        public BlobLeaseSingletonLock(ILogger<BlobLeaseSingletonLock> logger)
        {
            _logger = logger;

            string? connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                _container = new BlobContainerClient(connectionString, ContainerName);
                return;
            }

            // Identity-based configuration (AzureWebJobsStorage__blobServiceUri or __accountName)
            string? blobServiceUri = Environment.GetEnvironmentVariable("AzureWebJobsStorage__blobServiceUri");
            string? accountName    = Environment.GetEnvironmentVariable("AzureWebJobsStorage__accountName");

            Uri? containerUri = null;
            if (!string.IsNullOrWhiteSpace(blobServiceUri))
            {
                containerUri = new Uri(new Uri(blobServiceUri.TrimEnd('/') + "/"), ContainerName);
            }
            else if (!string.IsNullOrWhiteSpace(accountName))
            {
                // Adjust endpoint suffix for sovereign clouds (e.g., core.usgovcloudapi.net) if needed.
                containerUri = new Uri($"https://{accountName}.blob.core.windows.net/{ContainerName}");
            }

            if (containerUri is null)
            {
                throw new InvalidOperationException(
                    "AzureWebJobsStorage is not configured. Set 'AzureWebJobsStorage' (connection string) or " +
                    "'AzureWebJobsStorage__blobServiceUri' / 'AzureWebJobsStorage__accountName' (identity-based).");
            }

            _container = new BlobContainerClient(containerUri, new DefaultAzureCredential());
        }

        public async Task<IAsyncDisposable?> TryAcquireAsync(string lockName, CancellationToken cancellationToken = default)
        {
            string fullMethodName = nameof(BlobLeaseSingletonLock) + "." + (ExtensionHelper.GetMethodName() ?? "");

            await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            BlobClient blob = _container.GetBlobClient($"{lockName}.lock");

            // Ensure the blob exists (lease requires an existing blob).
            if (!await blob.ExistsAsync(cancellationToken))
            {
                try
                {
                    await blob.UploadAsync(BinaryData.FromString(string.Empty), overwrite: false, cancellationToken);
                }
                catch (RequestFailedException ex) when (ex.Status == 409)
                {
                    // Another instance created it first — fine.
                }
            }

            BlobLeaseClient leaseClient = blob.GetBlobLeaseClient();

            try
            {
                BlobLease lease = await leaseClient.AcquireAsync(LeaseDuration, cancellationToken: cancellationToken);
                _logger.DSLogInformation($"Acquired singleton lease '{lockName}' (LeaseId={lease.LeaseId}).", fullMethodName);
                return new LeaseHandle(leaseClient, lockName, _logger);
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                _logger.DSLogWarning($"Singleton lease '{lockName}' is already held by another instance. Skipping run.", fullMethodName);
                return null;
            }
        }

        private sealed class LeaseHandle : IAsyncDisposable
        {
            private readonly BlobLeaseClient _leaseClient;
            private readonly string _lockName;
            private readonly ILogger _logger;
            private readonly CancellationTokenSource _cts = new();
            private readonly Task _renewTask;
            private int _disposed;

            public LeaseHandle(BlobLeaseClient leaseClient, string lockName, ILogger logger)
            {
                _leaseClient = leaseClient;
                _lockName = lockName;
                _logger = logger;
                _renewTask = Task.Run(() => RenewLoopAsync(_cts.Token));
            }

            private async Task RenewLoopAsync(CancellationToken ct)
            {
                string fullMethodName = nameof(LeaseHandle) + "." + nameof(RenewLoopAsync);
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(RenewInterval, ct);
                        }
                        catch (TaskCanceledException) { return; }

                        try
                        {
                            await _leaseClient.RenewAsync(cancellationToken: ct);
                        }
                        catch (OperationCanceledException) { return; }
                        catch (Exception ex)
                        {
                            // If renew fails the lease will expire after LeaseDuration; the function should
                            // ideally be near-done. Log and keep trying until disposal.
                            _logger.DSLogException($"Failed to renew singleton lease '{_lockName}'.", ex, fullMethodName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.DSLogException($"Unexpected error in lease renewal loop for '{_lockName}'.", ex, fullMethodName);
                }
            }

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

                string fullMethodName = nameof(LeaseHandle) + "." + nameof(DisposeAsync);

                _cts.Cancel();
                try { await _renewTask; } catch { /* swallow */ }

                try
                {
                    await _leaseClient.ReleaseAsync();
                    _logger.DSLogInformation($"Released singleton lease '{_lockName}'.", fullMethodName);
                }
                catch (Exception ex)
                {
                    // Worst case: lease expires on its own after LeaseDuration.
                    _logger.DSLogException($"Failed to release singleton lease '{_lockName}'. It will expire automatically.", ex, fullMethodName);
                }
                finally
                {
                    _cts.Dispose();
                }
            }
        }
    }
}