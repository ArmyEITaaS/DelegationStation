using Azure.Core;
using Azure.Identity;
using DelegationStationShared;
using DelegationStationShared.Enums;
using DelegationStationShared.Extensions;
using DelegationStationShared.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace SyncedDeviceExport
{
    public class SyncedDeviceExportJob
    {
        private readonly ILogger _logger;
        private static Container? _container = null;

        public SyncedDeviceExportJob(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<SyncedDeviceExportJob>();
        }

        internal async Task RunAsync()
        {
            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Synced Device Export Job starting....", fullMethodName);

            ConnectToCosmosDb();
            if (_container == null)
            {
                _logger.DSLogError("Failed to connect to Cosmos DB, exiting.", fullMethodName);
                Environment.Exit(1);
            }

            int exported = await ExportSyncedDevicesAsync();

            _logger.DSLogInformation($"Synced Device Export Job done: Exported {exported} devices.", fullMethodName);
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

        private async Task<int> ExportSyncedDevicesAsync()
        {
            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            string outputDir = "D:\\home\\synced_device_export_outputs";
            if (!Directory.Exists(outputDir))
            {
                _logger.DSLogInformation($"Output path does not exist. Creating {outputDir}", fullMethodName);
                Directory.CreateDirectory(outputDir);
            }

            string fileName = $"SyncedDeviceExport_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            string filePath = Path.Combine(outputDir, fileName);
            _logger.DSLogInformation($"Export results will be written to: {filePath}", fullMethodName);

            List<Device> devices = await GetSyncedDevicesAsync();
            if (devices.Count == 0)
            {
                _logger.DSLogInformation("No Synced devices found. No file will be written.", fullMethodName);
                return 0;
            }

            await using var writer = new StreamWriter(filePath);
            writer.AutoFlush = true;
            await writer.WriteLineAsync("Make,Model,SerialNumber,Tag");

            foreach (var device in devices)
            {
                string tag = device.Tags.Count > 0 ? device.Tags[0] : string.Empty;
                string line = string.Join(",",
                    CsvEscape(device.Make),
                    CsvEscape(device.Model),
                    CsvEscape(device.SerialNumber),
                    CsvEscape(tag)
                );
                await writer.WriteLineAsync(line);
            }

            _logger.DSLogInformation($"Exported {devices.Count} Synced devices to {filePath}.", fullMethodName);
            return devices.Count;
        }

        private async Task<List<Device>> GetSyncedDevicesAsync()
        {
            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            var devices = new List<Device>();

            if (_container == null)
            {
                _logger.DSLogError("Cosmos container is null.", fullMethodName);
                return devices;
            }

            try
            {
                var query = new QueryDefinition("SELECT * FROM c WHERE c.Type = 'Device' AND c.Status = @status");
                query.WithParameter("@status", DeviceStatus.Synced);

                var iterator = _container.GetItemQueryIterator<Device>(query);
                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    devices.AddRange(response);
                }

                _logger.DSLogInformation($"Retrieved {devices.Count} Synced devices from Cosmos DB.", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to query Synced devices from Cosmos DB: ", ex, fullMethodName);
            }

            return devices;
        }

        private static string CsvEscape(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            return value;
        }
    }
}
