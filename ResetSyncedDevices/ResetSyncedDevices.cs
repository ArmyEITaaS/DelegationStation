using Azure.Core;
using Azure.Identity;
using DelegationStationShared;
using DelegationStationShared.Enums;
using DelegationStationShared.Extensions;
using DelegationStationShared.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace ResetSyncedDevices
{
    public class ResetSyncedDevices
    {
        private readonly ILogger _logger;
        private static Container? _container = null;

        public ResetSyncedDevices(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<ResetSyncedDevices>();
        }

        internal async Task RunAsync()
        {
            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Reset Synced Devices Job starting....", fullMethodName);

            ConnectToCosmosDb();
            if (_container == null)
            {
                _logger.DSLogError("Failed to connect to Cosmos DB, exiting.", fullMethodName);
                Environment.Exit(1);
            }

            int result = await ResetSyncedDevicesAsync();

            _logger.DSLogInformation($"Reset Synced Devices Job done: Reset {result} devices.", fullMethodName);
        }

        private void ConnectToCosmosDb()
        {
            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Connecting to Cosmos DB...", fullMethodName);

            string? containerName = Environment.GetEnvironmentVariable("COSMOS_CONTAINER_NAME");
            string? databaseName = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME");
            string? cosmosEndpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT");
            string? cosmosConnectionString = Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING");

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
            if (string.IsNullOrEmpty(cosmosEndpoint) && string.IsNullOrEmpty(cosmosConnectionString))
            {
                _logger.DSLogError("Cannot connect to CosmosDB. Missing required environment variable COSMOS_ENDPOINT or COSMOS_CONNECTION_STRING", fullMethodName);
                return;
            }

            try
            {
                CosmosClient client;
                if (!string.IsNullOrEmpty(cosmosConnectionString))
                {
                    _logger.DSLogInformation("Using connection string to connect to CosmosDB.", fullMethodName);
                    client = new CosmosClient(cosmosConnectionString);
                }
                else
                {
                    _logger.DSLogInformation("Using Managed Identity to connect to CosmosDB.", fullMethodName);
                    TokenCredential credential = new ManagedIdentityCredential();
                    client = new CosmosClient(cosmosEndpoint, credential);
                }
                _container = client.GetContainer(databaseName, containerName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to connect to CosmosDB: ", ex, fullMethodName);
                return;
            }

            _logger.DSLogInformation($"Connected to Cosmos DB database {databaseName} container {containerName}.", fullMethodName);
        }

        private async Task<int> ResetSyncedDevicesAsync()
        {
            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;
            int count = 0;

            int batchSize = 1000;
            string? batchSizeString = Environment.GetEnvironmentVariable("CORPID_RESET_BATCH_SIZE");
            if (!int.TryParse(batchSizeString, out int parsedBatchSize) || parsedBatchSize <= 0)
            {
                _logger.DSLogWarning($"CORPID_RESET_BATCH_SIZE is not set or invalid. Using default value: {batchSize}.", fullMethodName);
            }
            else
            {
                batchSize = parsedBatchSize;
                _logger.DSLogInformation($"Using CORPID_RESET_BATCH_SIZE: {batchSize}.", fullMethodName);
            }

            List<Device> devicesToReset = new List<Device>();

            try
            {
                QueryDefinition devicesQuery = new QueryDefinition(
                    "SELECT * FROM c WHERE c.Type = 'Device' AND c.Status = @status OFFSET 0 LIMIT @batchSize");
                devicesQuery.WithParameter("@status", DeviceStatus.Synced);
                devicesQuery.WithParameter("@batchSize", batchSize);

                if (_container == null)
                {
                    _logger.DSLogError("Cosmos container is null.", fullMethodName);
                    return count;
                }

                var deviceQueryIterator = _container.GetItemQueryIterator<Device>(devicesQuery);
                while (deviceQueryIterator.HasMoreResults)
                {
                    var result = await deviceQueryIterator.ReadNextAsync();
                    devicesToReset.AddRange(result);
                }

                _logger.DSLogInformation($"Found {devicesToReset.Count} Synced devices to reset.", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to query Cosmos for devices: ", ex, fullMethodName);
                return count;
            }

            foreach (var device in devicesToReset)
            {
                try
                {
                    IReadOnlyList<PatchOperation> patchOperations = new List<PatchOperation>()
                    {
                        PatchOperation.Replace("/CorporateIdentity", string.Empty),
                        PatchOperation.Replace("/CorporateIdentityID", string.Empty),
                        PatchOperation.Replace("/LastCorpIdentitySync", DateTime.MinValue),
                        PatchOperation.Replace("/Status", DeviceStatus.Added)
                    };

                    PartitionKey partitionKey = new PartitionKey(device.Id.ToString());
                    await _container.PatchItemAsync<Device>(device.Id.ToString(), partitionKey, patchOperations);
                    count++;
                    _logger.DSLogInformation($"Reset device {device.Id}: CorporateIdentity, CorporateIdentityID, LastCorpIdentitySync cleared and Status set to Added.", fullMethodName);
                }
                catch (Exception ex)
                {
                    _logger.DSLogException($"Failed to reset device {device.Id}: ", ex, fullMethodName);
                }
            }

            _logger.DSLogInformation($"Reset {count} devices.", fullMethodName);
            return count;
        }
    }
}
