using Azure.Core;
using Azure.Identity;
using DelegationStationShared;
using DelegationStationShared.Extensions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System.Security.Cryptography.X509Certificates;

namespace SyncDeviceNames
{
    public class SyncDeviceNamesJob
    {
        private readonly ILogger _logger;
        private static Container? _container = null;
        private static GraphServiceClient? _graphClient = null;

        public SyncDeviceNamesJob(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<SyncDeviceNamesJob>();
        }

        internal async Task RunAsync()
        {
            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Sync Device Names Job starting....", fullMethodName);

            ConnectToCosmosDb();
            if (_container == null)
            {
                _logger.DSLogError("Failed to connect to Cosmos DB, exiting.", fullMethodName);
                Environment.Exit(1);
            }

            ConnectToGraph();
            if (_graphClient == null)
            {
                _logger.DSLogError("Failed to connect to Graph, exiting.", fullMethodName);
                Environment.Exit(1);
            }

            int result = await SyncDeviceNamesAsync();

            _logger.DSLogInformation($"Sync Device Names Job done: Updated {result} devices.", fullMethodName);
        }

        private void ConnectToCosmosDb()
        {

            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Connecting to Cosmos DB...", fullMethodName);

            string? containerName = Environment.GetEnvironmentVariable("COSMOS_CONTAINER_NAME");
            string? databaseName = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME");
            var cosmosEndpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT");

            if (string.IsNullOrEmpty(containerName))
            {
                _logger.DSLogWarning("COSMOS_CONTAINER_NAME is null or empty, using default value of DeviceData", fullMethodName);
                containerName = "DeviceData";
            }
            if (string.IsNullOrEmpty(databaseName))
            {
                _logger.DSLogWarning("COSMOS_DATABASE_NAME is null or empty, using default value of DelegationStationData", fullMethodName);
                databaseName = "DelegationStationData";
            }
            if (string.IsNullOrEmpty(cosmosEndpoint))
            {
                _logger.DSLogError("Cannot connect to CosmosDB. Missing required environment variable COSMOS_CONNECTION_STRING", fullMethodName);
                return;
            }

            try
            {
                TokenCredential credential = new ManagedIdentityCredential();
                CosmosClient client = new(accountEndpoint: cosmosEndpoint, credential);
                _container = client.GetContainer(databaseName, containerName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to connect to CosmosDB: ", ex, fullMethodName);
                return;
            }

            _logger.DSLogInformation($"Connected to Cosmos DB database {databaseName} container {containerName}.", fullMethodName);
        }

        private void ConnectToGraph()
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Connecting to Graph...", fullMethodName);

            var azureCloud = Environment.GetEnvironmentVariable("AzureEnvironment");
            var graphEndpoint = Environment.GetEnvironmentVariable("GraphEndpoint");
            var options = new TokenCredentialOptions
            {
                AuthorityHost = azureCloud == "AzurePublicCloud" ? AzureAuthorityHosts.AzurePublicCloud : AzureAuthorityHosts.AzureGovernment
            };

            var scopes = new string[] { $"{graphEndpoint}.default" };
            string baseUrl = graphEndpoint + "v1.0";

            var certDN = Environment.GetEnvironmentVariable("CertificateDistinguishedName");

            if (!String.IsNullOrEmpty(certDN))
            {
                _logger.DSLogInformation("Using certificate authentication", fullMethodName);
                _logger.DSLogDebug("AzureCloud: " + azureCloud, fullMethodName);
                _logger.DSLogDebug("GraphEndpoint: " + graphEndpoint, fullMethodName);

                X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);
                _logger.DSLogInformation("Using certificate with Subject Name {0} for Graph service: " + certDN, fullMethodName);
                var certificate = store.Certificates.Cast<X509Certificate2>().FirstOrDefault(cert => cert.Subject.ToString() == certDN);

                var clientCertCredential = new ClientCertificateCredential(
          Environment.GetEnvironmentVariable("AzureAd__TenantId"),
           Environment.GetEnvironmentVariable("AzureAd__ClientId"),
           certificate,
             options
        );
                store.Close();
                _graphClient = new GraphServiceClient(clientCertCredential, scopes, baseUrl);
            }
            else
            {
                _logger.DSLogInformation("Using Client Secret for Graph service", fullMethodName);
                _logger.DSLogDebug("AzureCloud: " + azureCloud, fullMethodName);
                _logger.DSLogDebug("GraphEndpoint: " + graphEndpoint, fullMethodName);

                var clientSecretCredential = new ClientSecretCredential(
           Environment.GetEnvironmentVariable("AzureAd__TenantId"),
      Environment.GetEnvironmentVariable("AzureAd__ClientId"),
    Environment.GetEnvironmentVariable("AzureApp__ClientSecret"),
        options
            );

                _graphClient = new GraphServiceClient(clientSecretCredential, scopes, baseUrl);
            }
        }

        private async Task<int> SyncDeviceNamesAsync()
        {
            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;
            int updateCount = 0;
            int notFoundCount = 0;
            int skippedCount = 0;

            try
            {
                _logger.DSLogInformation("Retrieving all managed devices from Microsoft Graph...", fullMethodName);

                // Get all managed devices from Graph with Make, Model, SerialNumber, and DeviceName
                List<ManagedDevice> managedDevices = new List<ManagedDevice>();

                var devices = await _graphClient!.DeviceManagement.ManagedDevices .GetAsync((requestConfiguration) =>
                   {
                      requestConfiguration.QueryParameters.Select = new string[] { "id", "manufacturer", "model", "serialNumber", "deviceName" };
                   });

                if (devices == null)
                {
                    _logger.DSLogWarning("No managed devices found in Graph.", fullMethodName);
                    return 0;
                }

                var pageIterator = PageIterator<ManagedDevice, ManagedDeviceCollectionResponse>
                     .CreatePageIterator(_graphClient, devices, (device) =>
                    {
                        managedDevices.Add(device);
                        return true;
                    });

                await pageIterator.IterateAsync();

                _logger.DSLogInformation($"Retrieved {managedDevices.Count} managed devices from Graph.", fullMethodName);

                // For each managed device, check if it exists in Cosmos DB and update PreferredHostname if needed
                foreach (var managedDevice in managedDevices)
                {
                    _logger.DSLogInformation("Processing Device: " + managedDevice.Id + " " + managedDevice.Manufacturer + " " + managedDevice.Model + " " + managedDevice.SerialNumber + " " + managedDevice.DeviceName);

                    // Skip devices without complete information
                    if (string.IsNullOrEmpty(managedDevice.Manufacturer) ||
                         string.IsNullOrEmpty(managedDevice.Model) ||
                     string.IsNullOrEmpty(managedDevice.SerialNumber))
                    {
                        _logger.DSLogWarning($"Skipping device {managedDevice.Id} - missing M/M/SN", fullMethodName);
                        skippedCount++;
                        continue;
                    }

                    // Skip devices without a device name
                    if (string.IsNullOrEmpty(managedDevice.DeviceName))
                    {
                        _logger.DSLogWarning($"Skipping device {managedDevice.Id} - no device name in Graph", fullMethodName);
                        skippedCount++;
                        continue;
                    }

                    try
                    {
                        // Query Cosmos DB for matching device
                        QueryDefinition deviceQuery = new QueryDefinition(
                             "SELECT * FROM c WHERE c.Type='Device' " +
                                 "AND LOWER(c.Make) = LOWER(@make) " +
                        "AND LOWER(c.Model) = LOWER(@model) " +
                           "AND LOWER(c.SerialNumber) = LOWER(@serialNumber)")
                               .WithParameter("@make", managedDevice.Manufacturer.Trim())
                               .WithParameter("@model", managedDevice.Model.Trim())
                       .WithParameter("@serialNumber", managedDevice.SerialNumber.Trim());

                        var deviceQueryIterator = _container!.GetItemQueryIterator<DelegationStationShared.Models.Device>(deviceQuery);
                        DelegationStationShared.Models.Device? cosmosDevice = null;

                        while (deviceQueryIterator.HasMoreResults)
                        {
                            var result = await deviceQueryIterator.ReadNextAsync();
                            cosmosDevice = result.FirstOrDefault();
                            break; // Only get the first match
                        }

                        if (cosmosDevice == null)
                        {
                            _logger.DSLogWarning($"No matching device found in Cosmos DB for: {managedDevice.Manufacturer} {managedDevice.Model} {managedDevice.SerialNumber}", fullMethodName);
                            notFoundCount++;
                            continue;
                        }

                        // Check if PreferredHostname is missing or empty
                        if (string.IsNullOrEmpty(cosmosDevice.PreferredHostname))
                        {
                            _logger.DSLogInformation($"Updating device {cosmosDevice.Id} PreferredHostname to '{managedDevice.DeviceName}'", fullMethodName);

                            // Update PreferredHostname using PATCH operation
                            PatchOperation patchOperation = PatchOperation.Add("/PreferredHostname", managedDevice.DeviceName);
                            IReadOnlyList<PatchOperation> patchOperations = new List<PatchOperation>() { patchOperation };
                            PartitionKey partitionKey = new PartitionKey(cosmosDevice.Id.ToString());

                            await _container.PatchItemAsync<DelegationStationShared.Models.Device>(cosmosDevice.Id.ToString(), partitionKey, patchOperations);
                            updateCount++;
                        }
                        else
                        {
                            _logger.DSLogInformation($"Device {cosmosDevice.Id} already has PreferredHostname '{cosmosDevice.PreferredHostname}', skipping", fullMethodName);
                            skippedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.DSLogException($"Error processing device {managedDevice.Manufacturer} {managedDevice.Model} {managedDevice.SerialNumber}", ex, fullMethodName);
                    }
                }

                _logger.DSLogInformation($"Sync completed. Updated: {updateCount}, Not Found: {notFoundCount}, Skipped: {skippedCount}", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to sync device names", ex, fullMethodName);
            }

            return updateCount;
        }
    }
}
