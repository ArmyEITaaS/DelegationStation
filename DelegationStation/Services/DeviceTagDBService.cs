using Azure.Core;
using Azure.Identity;
using DelegationStation.Interfaces;
using DelegationStationShared.Models;
using Microsoft.Azure.Cosmos;

namespace DelegationStation.Services
{
    public class DeviceTagDBService : IDeviceTagDBService
    {
        private readonly ILogger? _logger;
        private readonly Container _container;
        private string? _DefaultGroup;

        public DeviceTagDBService(IConfiguration configuration, ILogger<DeviceDBService> logger)
        {
            this._logger = logger;
            if (configuration == null)
            {
                throw new Exception("DeviceDBService appsettings configuration is null.");
            }

            string cosmosEndpoint = configuration.GetSection("COSMOS_ENDPOINT").Value ?? "";
            string cosmosConnectionString = configuration.GetSection("COSMOS_CONNECTION_STRING").Value ?? "";

            if (string.IsNullOrEmpty(cosmosConnectionString) && string.IsNullOrEmpty(cosmosEndpoint))
            {
                throw new Exception("DeviceDBService appsettings COSMOS_CONNECTION_STRING and COSMOS_ENDPOINT settings are both null or empty. At least one must be set.");
            }
            if (string.IsNullOrEmpty(configuration.GetSection("DefaultAdminGroupObjectId").Value))
            {
                throw new Exception("DefaultAdminGroupObjectId appsettings is null or empty");
            }
            if (string.IsNullOrEmpty(configuration.GetSection("COSMOS_DATABASE_NAME").Value))
            {
                _logger.LogInformation("COSMOS_DATABASE_NAME is null or empty, using default value of DelegationStationData");
            }
            if (string.IsNullOrEmpty(configuration.GetSection("COSMOS_CONTAINER_NAME").Value))
            {
                _logger.LogInformation("COSMOS_CONTAINER_NAME is null or empty, using default value of DeviceData");
            }

            string dbName = string.IsNullOrEmpty(configuration.GetSection("COSMOS_DATABASE_NAME").Value) ? "DelegationStationData" : configuration.GetSection("COSMOS_DATABASE_NAME").Value!;
            string containerName = string.IsNullOrEmpty(configuration.GetSection("COSMOS_CONTAINER_NAME").Value) ? "DeviceData" : configuration.GetSection("COSMOS_CONTAINER_NAME").Value!;


            CosmosClient client;
            if (!string.IsNullOrEmpty(cosmosEndpoint))
            {
                logger.LogInformation("Using Managed Identity to connect to CosmosDB");
                TokenCredential credential = new ManagedIdentityCredential();
                client = new CosmosClient(cosmosEndpoint, credential);
            }
            else
            {
                logger.LogInformation("Using Connection String to connect to CosmosDB");
                client = new(
                    connectionString: configuration.GetSection("COSMOS_CONNECTION_STRING").Value!
                );
            }
            ConfigureCosmosDatabase(client, dbName, containerName);
            this._container = client.GetContainer(dbName, containerName);
            _DefaultGroup = configuration.GetSection("DefaultAdminGroupObjectId").Value;
        }

        public async void ConfigureCosmosDatabase(CosmosClient client, string databaseName, string containerName)
        {
            DatabaseResponse database = await client.CreateDatabaseIfNotExistsAsync(databaseName);
            await database.Database.CreateContainerIfNotExistsAsync(containerName, "/PartitionKey");
        }

        public async Task<List<DeviceTag>> GetDeviceTagsByPageAsync(IEnumerable<string> groupIds, int pageNumber, int pageSize)
        {

            List<DeviceTag> deviceTags = new List<DeviceTag>();

            groupIds = groupIds.Where(g => System.Text.RegularExpressions.Regex.Match(g, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success);

            if (groupIds.Count() < 1)
            {
                return deviceTags;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int argCount = 0;

            if (groupIds.Contains(_DefaultGroup))
            {
                sb.Append("SELECT * FROM t WHERE t.PartitionKey = \"DeviceTag\"");
            }
            else
            {
                sb.Append("SELECT DISTINCT t.id,t.Name,t.Description,t.RoleDelegations,t.UpdateActions,t.PartitionKey,t.Type FROM t JOIN r IN t.RoleDelegations WHERE t.PartitionKey = \"DeviceTag\" AND (");

                foreach (string groupId in groupIds)
                {
                    sb.Append($"CONTAINS(r.SecurityGroupId, @arg{argCount}, true) ");
                    if (groupId != groupIds.Last())
                    {
                        sb.Append("OR ");
                    }
                    argCount++;
                }
                sb.Append(")");
            }
            sb.Append($" OFFSET {(pageNumber - 1) * pageSize} LIMIT {pageSize}");


            argCount = 0;
            QueryDefinition q = new QueryDefinition(sb.ToString());

            if (!groupIds.Contains(_DefaultGroup))
            {
                foreach (string groupId in groupIds)
                {
                    q.WithParameter($"@arg{argCount}", groupId);
                    argCount++;
                }
            }

            var queryIterator = this._container.GetItemQueryIterator<DeviceTag>(q);
            while (queryIterator.HasMoreResults)
            {
                var response = await queryIterator.ReadNextAsync();
                deviceTags.AddRange(response.ToList());
            }

            return deviceTags;

        }

        public async Task<List<DeviceTag>> GetDeviceTagsAsync(IEnumerable<string> groupIds)
        {
            List<DeviceTag> deviceTags = new List<DeviceTag>();

            groupIds = groupIds.Where(g => System.Text.RegularExpressions.Regex.Match(g, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success);

            if (groupIds.Count() < 1)
            {
                throw new Exception("DeviceTagDBService GetDeviceTagsAsync no valid group ids sent.");
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int argCount = 0;

            if (groupIds.Contains(_DefaultGroup))
            {
                sb.Append("SELECT * FROM t WHERE t.PartitionKey = \"DeviceTag\"");
            }
            else
            {
                sb.Append("SELECT DISTINCT t.id,t.Name,t.Description,t.RoleDelegations,t.UpdateActions,t.PartitionKey,t.Type FROM t JOIN r IN t.RoleDelegations WHERE t.PartitionKey = \"DeviceTag\" AND (");

                foreach (string groupId in groupIds)
                {
                    sb.Append($"CONTAINS(r.SecurityGroupId, @arg{argCount}, true) ");
                    if (groupId != groupIds.Last())
                    {
                        sb.Append("OR ");
                    }
                    argCount++;
                }
                sb.Append(")");
            }


            argCount = 0;
            QueryDefinition q = new QueryDefinition(sb.ToString());

            if (!groupIds.Contains(_DefaultGroup))
            {
                foreach (string groupId in groupIds)
                {
                    q.WithParameter($"@arg{argCount}", groupId);
                    argCount++;
                }
            }

            var queryIterator = this._container.GetItemQueryIterator<DeviceTag>(q);
            while (queryIterator.HasMoreResults)
            {
                var response = await queryIterator.ReadNextAsync();
                deviceTags.AddRange(response.ToList());
            }

            return deviceTags;
        }
        public async Task<int> GetDeviceTagCountAsync(IEnumerable<string> groupIds)
        {
            int numTags = 0;
            groupIds = groupIds.Where(g => System.Text.RegularExpressions.Regex.Match(g, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success);

            if (groupIds.Count() < 1)
            {
                return 0;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int argCount = 0;

            if (groupIds.Contains(_DefaultGroup))
            {
                sb.Append("SELECT VALUE COUNT(1) FROM t WHERE t.PartitionKey = \"DeviceTag\"");
            }
            else
            {
                sb.Append("SELECT VALUE COUNT(1) FROM (SELECT DISTINCT t.id,t.Name,t.Description,t.RoleDelegations,t.UpdateActions,t.PartitionKey,t.Type FROM t JOIN r IN t.RoleDelegations WHERE t.PartitionKey = \"DeviceTag\" AND (");

                foreach (string groupId in groupIds)
                {
                    sb.Append($"CONTAINS(r.SecurityGroupId, @arg{argCount}, true) ");
                    if (groupId != groupIds.Last())
                    {
                        sb.Append("OR ");
                    }
                    argCount++;
                }
                sb.Append("))");
            }

            argCount = 0;
            QueryDefinition q = new QueryDefinition(sb.ToString());

            if (!groupIds.Contains(_DefaultGroup))
            {
                foreach (string groupId in groupIds)
                {
                    q.WithParameter($"@arg{argCount}", groupId);
                    argCount++;
                }
            }

            var queryIterator = this._container.GetItemQueryIterator<int>(q);
            if (queryIterator.HasMoreResults)
            {
                FeedResponse<int> response = await queryIterator.ReadNextAsync();
                numTags = response.FirstOrDefault();
            }

            return numTags;

        }


        public async Task<DeviceTag> GetDeviceTagAsync(string tagId)
        {
            if (tagId == null)
            {
                throw new Exception("DeviceDBService GetDeviceAsync was sent null tagId");
            }

            if (!System.Text.RegularExpressions.Regex.Match(tagId, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success)
            {
                throw new Exception($"DeviceDBService GetDeviceAsync tagId did not match GUID format {tagId}");
            }

            ItemResponse<DeviceTag> response = await this._container.ReadItemAsync<DeviceTag>(tagId, new PartitionKey("DeviceTag"));
            return response.Resource;
        }


       

        public async Task<int> GetDeviceCountByTagIdAsync(string tagId)
        {
            if (tagId == null)
            {
                throw new Exception("DeviceDBService GetDeviceAsync was sent null tagId");
            }
            if (!System.Text.RegularExpressions.Regex.Match(tagId, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success)
            {
                throw new Exception($"DeviceDBService GetDeviceAsync tagId did not match GUID format {tagId}");
            }



            QueryDefinition q = new QueryDefinition("SELECT VALUE COUNT(d.id) FROM d WHERE d.Type = \"Device\" AND ARRAY_CONTAINS(d.Tags, @tagId, true)");
            q.WithParameter("@tagId", tagId);

            FeedIterator<int> queryIterator = this._container.GetItemQueryIterator<int>(q);
            FeedResponse<int> response = await queryIterator.ReadNextAsync();

            return response.Resource.FirstOrDefault<int>();

        }

        public async Task<DeviceTag> AddOrUpdateDeviceTagAsync(DeviceTag deviceTag)
        {
            ItemResponse<DeviceTag> response = await this._container.UpsertItemAsync<DeviceTag>(deviceTag);
            return response;
        }

        public async Task DeleteDeviceTagAsync(DeviceTag deviceTag)
        {
            await this._container.DeleteItemAsync<DeviceTag>(deviceTag.Id.ToString(), new PartitionKey(deviceTag.PartitionKey));
        }
    }
}