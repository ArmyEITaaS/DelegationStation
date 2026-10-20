using Microsoft.Extensions.Logging;
using Microsoft.Azure.Cosmos;
using CorporateIdentifierSync.Interfaces;
using DelegationStationShared.Extensions;
using Device = DelegationStationShared.Models.Device;
using DelegationStationShared;
using DelegationStationShared.Models;
using Azure.Core;
using Azure.Identity;
using DelegationStationShared.Enums;

namespace CorporateIdentifierSync.Services
{
    internal class CosmosDbService : ICosmosDbService
    {
        private readonly ILogger<CosmosDbService> _logger;
        private readonly CosmosClient _cosmosClient;
        private readonly Container _container;

        public CosmosDbService(ILogger<CosmosDbService> logger)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger = logger;

            string containerName = Environment.GetEnvironmentVariable("COSMOS_CONTAINER_NAME", EnvironmentVariableTarget.Process) ?? "";
            string databaseName = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME", EnvironmentVariableTarget.Process) ?? "";
            var connectionString = Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING", EnvironmentVariableTarget.Process) ?? "";
            string cosmosEndpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT", EnvironmentVariableTarget.Process) ?? "";

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
            if (String.IsNullOrEmpty(connectionString) && String.IsNullOrEmpty(cosmosEndpoint))
            {
                _logger.DSLogError("Cannot connect to CosmosDB. Must configure COSMOS_CONNECTION_STRING or COSMOS_ENDPOINT", fullMethodName);
                return;
            }

            try
            {
                if (_cosmosClient == null)
                {
                    if (!String.IsNullOrEmpty(cosmosEndpoint))
                    {
                        _logger.DSLogInformation("Using Managed Identity to connect to CosmosDB", fullMethodName);
                        TokenCredential credential = new ManagedIdentityCredential();
                        _cosmosClient = new CosmosClient(cosmosEndpoint, credential);
                    }
                    else
                    {
                        _logger.DSLogInformation("Using connection string to connect to CosmosDB", fullMethodName);
                        _cosmosClient = new CosmosClient(connectionString);
                    }
                    _container = _cosmosClient.GetContainer(databaseName, containerName);
                }
                else if (_container == null)
                {
                    _container = _cosmosClient.GetContainer(databaseName, containerName);
                }

            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to connect to CosmosDB", ex, fullMethodName);
            }

            _logger.DSLogInformation("Connected to Cosmos DB database " + databaseName + " container " + containerName + ".", fullMethodName);
        }



        public async Task<List<Device>> GetAddedDevices(int batchSize)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation($"Getting next {batchSize} devices with status Added.", fullMethodName);

            // To ensure previously added devices are processed, check for devices without status as well as those set to Added
            // to handle initially large number of devices and our large bulk uploads, limiting processing to 40K devices at a time
            // TODO
            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.Type = \"Device\" " +
                "AND (NOT IS_DEFINED(c.Status) OR (c.Status = @status)) ORDER BY c.ModifiedUTC DESC " +
                "OFFSET 0 LIMIT @batchSize");
            query.WithParameter("@status", DeviceStatus.Added);
            query.WithParameter("@batchSize", batchSize);

            var queryIterator = _container.GetItemQueryIterator<Device>(query);

            List<Device> devices = new List<Device>();
            while (queryIterator.HasMoreResults)
            {
                var response = await queryIterator.ReadNextAsync();
                devices.AddRange(response.ToList());
            }

            return devices;

        }
        public async Task<List<Device>> GetAddedDevicesToSync(List<string> tagIds, int batchSize)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            if (tagIds.Count == 0)
            {
                _logger.DSLogInformation("No tag IDs provided. Returning empty list.", fullMethodName);
                return new List<Device>();
            }

            _logger.DSLogInformation($"Getting up to {batchSize} Added devices in {tagIds.Count} sync-enabled tag(s).", fullMethodName);

            string tagFilter = string.Join(" OR ", tagIds.Select((_, i) => $"t = @tag{i}"));

            QueryDefinition query = new QueryDefinition(
                "SELECT * FROM c WHERE c.Type = \"Device\" " +
                "AND (NOT IS_DEFINED(c.Status) OR c.Status = @status) " +
                $"AND EXISTS(SELECT VALUE t FROM t IN c.Tags WHERE {tagFilter}) " +
                "ORDER BY c.ModifiedUTC ASC " +
                "OFFSET 0 LIMIT @batchSize");

            query.WithParameter("@status", DeviceStatus.Added);
            query.WithParameter("@batchSize", batchSize);
            for (int i = 0; i < tagIds.Count; i++)
            {
                query.WithParameter($"@tag{i}", tagIds[i]);
            }

            var queryIterator = _container.GetItemQueryIterator<Device>(query);
            List<Device> devices = new List<Device>();
            try
            {
                while (queryIterator.HasMoreResults)
                {
                    var response = await queryIterator.ReadNextAsync();
                    devices.AddRange(response.ToList());
                }
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to query Cosmos DB for Added devices in sync-enabled tags.", ex, fullMethodName);
            }
            return devices;
        }

        public async Task<List<Device>> GetAddedDevicesNotSyncing(List<string> tagIds, int batchSize)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            if (tagIds.Count == 0)
            {
                _logger.DSLogInformation("No tag IDs provided. Returning empty list.", fullMethodName);
                return new List<Device>();
            }

            _logger.DSLogInformation($"Getting up to {batchSize} Added devices in {tagIds.Count} sync-disabled tag(s).", fullMethodName);

            string tagFilter = string.Join(" OR ", tagIds.Select((_, i) => $"t = @tag{i}"));

            QueryDefinition query = new QueryDefinition(
                "SELECT * FROM c WHERE c.Type = \"Device\" " +
                "AND (NOT IS_DEFINED(c.Status) OR c.Status = @status) " +
                $"AND EXISTS(SELECT VALUE t FROM t IN c.Tags WHERE {tagFilter}) " +
                "ORDER BY c.ModifiedUTC ASC " +
                "OFFSET 0 LIMIT @batchSize");

            query.WithParameter("@status", DeviceStatus.Added);
            query.WithParameter("@batchSize", batchSize);
            for (int i = 0; i < tagIds.Count; i++)
            {
                query.WithParameter($"@tag{i}", tagIds[i]);
            }

            var queryIterator = _container.GetItemQueryIterator<Device>(query);
            List<Device> devices = new List<Device>();
            try
            {
                while (queryIterator.HasMoreResults)
                {
                    var response = await queryIterator.ReadNextAsync();
                    devices.AddRange(response.ToList());
                }
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to query Cosmos DB for Added devices in sync-disabled tags.", ex, fullMethodName);
            }
            return devices;
        }

        public async Task<List<Device>> GetDevicesMarkedForDeletion()
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.Type = \"Device\" AND c.Status = @status");
            query.WithParameter("@status", DeviceStatus.Deleting);


            var queryIterator = _container.GetItemQueryIterator<Device>(query);

            List<Device> devices = new List<Device>();
            try
            {
                while (queryIterator.HasMoreResults)
                {
                    var response = await queryIterator.ReadNextAsync();
                    devices.AddRange(response.ToList());
                }
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failure querying Cosmos DB for devices missing CorporateIdentityID.\n", ex, fullMethodName);
            }

            return devices;
        }

        public async Task UpdateDevice(Device device)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation($"Updating device {device.Make} {device.Model} {device.SerialNumber}.", fullMethodName);

            var options = string.IsNullOrEmpty(device.ETag)
                ? null
                : new ItemRequestOptions { IfMatchEtag = device.ETag };

            ItemResponse<Device> response = await _container.ReplaceItemAsync<Device>(
                device, device.Id.ToString(), new PartitionKey(device.PartitionKey), options);

            device.ETag = response.ETag;

            _logger.DSLogInformation($"Updated device {device.Make} {device.Model} {device.SerialNumber}.", fullMethodName);
        }

        public async Task<Device?> GetDevice(Guid id, string partitionKey)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            try
            {
                var response = await _container.ReadItemAsync<Device>(
                    id.ToString(), new PartitionKey(partitionKey));
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.DSLogInformation($"Device {id} not found.", fullMethodName);
                return null;
            }
        }

        public async Task DeleteDevice(Device device)
        {
            await _container.DeleteItemAsync<Device>(device.Id.ToString(), new PartitionKey(device.PartitionKey));
        }

        public async Task<List<Device>> GetDevicesSyncedBefore(DateTime date)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            // Only return devices that are in Synced or NotSyncing status
            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.Type = \"Device\" AND NOT (c.Status = @deleting)" +
                " AND NOT (c.Status = @added) AND c.LastCorpIdentitySync <= @date");
            query.WithParameter("@deleting", DeviceStatus.Deleting);
            query.WithParameter("@added", DeviceStatus.Added);
            query.WithParameter("@date", date);
            var queryIterator = _container.GetItemQueryIterator<Device>(query);
            List<Device> devices = new List<Device>();
            try
            {
                while (queryIterator.HasMoreResults)
                {
                    var response = await queryIterator.ReadNextAsync();
                    devices.AddRange(response.ToList());
                }
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failure querying Cosmos DB for devices missing CorporateIdentityID.\n", ex, fullMethodName);
            }
            return devices;

        }

        public async Task<List<Device>> GetSyncedDevicesSyncedBefore(DateTime date)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            // Only return devices that are in Synced status
            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.Type = \"Device\" AND c.Status = @status AND c.LastCorpIdentitySync <= @date");
            query.WithParameter("@status", DeviceStatus.Synced);
            query.WithParameter("@date", date);
            var queryIterator = _container.GetItemQueryIterator<Device>(query);
            List<Device> devices = new List<Device>();
            try
            {
                while (queryIterator.HasMoreResults)
                {
                    var response = await queryIterator.ReadNextAsync();
                    devices.AddRange(response.ToList());
                }
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failure querying Cosmos DB for devices missing CorporateIdentityID.\n", ex, fullMethodName);
            }
            return devices;

        }

        public async Task<List<Device>> GetNotSyncingDevicesInTags(List<string> tagIds, int batchSize)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            if (tagIds.Count == 0)
            {
                _logger.DSLogInformation("No tag IDs provided. Returning empty list.", fullMethodName);
                return new List<Device>();
            }

            _logger.DSLogInformation($"Getting up to {batchSize} NotSyncing devices in {tagIds.Count} enabled tag(s).", fullMethodName);

            string tagFilter = string.Join(" OR ", tagIds.Select((_, i) => $"t = @tag{i}"));

            QueryDefinition query = new QueryDefinition(
                "SELECT * FROM c WHERE c.Type = \"Device\" AND c.Status = @status " +
                $"AND EXISTS(SELECT VALUE t FROM t IN c.Tags WHERE {tagFilter}) " +
                "ORDER BY c.ModifiedUTC ASC " +
                "OFFSET 0 LIMIT @batchSize");

            query.WithParameter("@status", DeviceStatus.NotSyncing);
            query.WithParameter("@batchSize", batchSize);
            for (int i = 0; i < tagIds.Count; i++)
            {
                query.WithParameter($"@tag{i}", tagIds[i]);
            }

            var queryIterator = _container.GetItemQueryIterator<Device>(query);
            List<Device> devices = new List<Device>();
            try
            {
                while (queryIterator.HasMoreResults)
                {
                    var response = await queryIterator.ReadNextAsync();
                    devices.AddRange(response.ToList());
                }
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to query Cosmos DB for NotSyncing devices in enabled tags.", ex, fullMethodName);
            }
            return devices;
        }

        public async Task<List<Device>> GetSyncedDevicesInTags(List<string> tagIds, int batchSize)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            if (tagIds.Count == 0)
            {
                _logger.DSLogInformation("No tag IDs provided. Returning empty list.", fullMethodName);
                return new List<Device>();
            }

            _logger.DSLogInformation($"Getting up to {batchSize} Synced devices in {tagIds.Count} disabled tag(s).", fullMethodName);

            string tagFilter = string.Join(" OR ", tagIds.Select((_, i) => $"t = @tag{i}"));

            QueryDefinition query = new QueryDefinition(
                "SELECT * FROM c WHERE c.Type = \"Device\" AND c.Status = @status " +
                $"AND EXISTS(SELECT VALUE t FROM t IN c.Tags WHERE {tagFilter}) " +
                "ORDER BY c.ModifiedUTC ASC " +
                "OFFSET 0 LIMIT @batchSize");

            query.WithParameter("@status", DeviceStatus.Synced);
            query.WithParameter("@batchSize", batchSize);
            for (int i = 0; i < tagIds.Count; i++)
            {
                query.WithParameter($"@tag{i}", tagIds[i]);
            }

            var queryIterator = _container.GetItemQueryIterator<Device>(query);
            List<Device> devices = new List<Device>();
            try
            {
                while (queryIterator.HasMoreResults)
                {
                    var response = await queryIterator.ReadNextAsync();
                    devices.AddRange(response.ToList());
                }
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to query Cosmos DB for Synced devices in disabled tags.", ex, fullMethodName);
            }
            return devices;
        }

        public async Task<DelegationStationShared.Models.DeviceTag> GetDeviceTag(string id)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            if (id == null)
            {
                throw new Exception($"{fullMethodName} was sent null tag ID");
            }

            if (!System.Text.RegularExpressions.Regex.Match(id, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success)
            {
                throw new Exception($"{fullMethodName} tag ID did not match GUID format {id}");
            }

            ItemResponse<DeviceTag> response = await this._container.ReadItemAsync<DeviceTag>(id, new PartitionKey("DeviceTag"));
            return response.Resource;

        }

        public async Task<List<string>> GetSyncingDeviceTags()
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.PartitionKey= \"DeviceTag\" AND c.CorpIDSyncEnabled=true");
            var queryIterator = _container.GetItemQueryIterator<DeviceTag>(query);
            List<string> tagIDs = new List<string>();
            try
            {
                while (queryIterator.HasMoreResults)
                {
                    var response = await queryIterator.ReadNextAsync();
                    tagIDs.AddRange(response.Select(t => t.Id.ToString()).ToList());
                }
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failure querying Cosmos DB for devices missing CorporateIdentityID.\n", ex, fullMethodName);
            }
            return tagIDs;

        }

        public async Task<List<string>> GetNonSyncingDeviceTags()
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            QueryDefinition query = new QueryDefinition(
                "SELECT * FROM c WHERE c.PartitionKey = \"DeviceTag\" AND (NOT IS_DEFINED(c.CorpIDSyncEnabled) OR c.CorpIDSyncEnabled = false)");
            var queryIterator = _container.GetItemQueryIterator<DeviceTag>(query);
            List<string> tagIDs = new List<string>();
            try
            {
                while (queryIterator.HasMoreResults)
                {
                    var response = await queryIterator.ReadNextAsync();
                    tagIDs.AddRange(response.Select(t => t.Id.ToString()).ToList());
                }
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failure querying Cosmos DB for sync-disabled device tags.", ex, fullMethodName);
            }
            return tagIDs;
        }

        public async Task<CorpIDCounter> GetCorpIDCounter()
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Getting CorpIDCounter from Cosmos DB.", fullMethodName);

            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.PartitionKey = \"CorpIDCounter\"");
            var queryIterator = _container.GetItemQueryIterator<CorpIDCounter>(query);
            while (queryIterator.HasMoreResults)
            {
                var response = await queryIterator.ReadNextAsync();
                var counter = response.FirstOrDefault();
                if (counter != null)
                {
                    _logger.DSLogInformation($"CorpIDCounter found: {counter}.", fullMethodName);
                    return counter;
                }
            }

            // Counter document does not exist — throw rather than returning a default zero counter,
            // which would incorrectly imply full capacity is available.
            _logger.DSLogError("CorpIDCounter not found in Cosmos DB. Cannot safely continue without a valid counter.", fullMethodName);
            throw new InvalidOperationException("CorpIDCounter document was not found in Cosmos DB.");
        }

        public async Task<bool> TrySetCorpIDCounter(CorpIDCounter counter, string etag)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            counter.ModifiedDT = DateTime.UtcNow;

            _logger.DSLogInformation($"Upserting CorpIDCounter: {counter}.", fullMethodName);

            try
            {
                var options = new ItemRequestOptions { IfMatchEtag = etag };
                await _container.UpsertItemAsync(counter, new PartitionKey(counter.PartitionKey), options);

                _logger.DSLogInformation("CorpIDCounter updated successfully.", fullMethodName);
                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
            {
                _logger.DSLogInformation("Counter changed before updated requested. Should be handled by calling code.", fullMethodName);
                return false;
            }
            // TBD:  do we want to handle any other exception types in here??

        }

        public async Task<int> GetSyncedDeviceCountAsync()
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Getting count of devices with status Synced.", fullMethodName);

            try
            {
                QueryDefinition query = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.Type = \"Device\" AND c.Status = @status");
                query.WithParameter("@status", DeviceStatus.Synced);

                var queryIterator = _container.GetItemQueryIterator<int>(query);
                int count = 0;

                while (queryIterator.HasMoreResults)
                {
                    var response = await queryIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        count = item;
                    }
                }

                _logger.DSLogInformation($"Found {count} devices with status Synced.", fullMethodName);
                return count;
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to query Cosmos DB for synced device count.", ex, fullMethodName);
                return 0;
            }
        }

    }
}
