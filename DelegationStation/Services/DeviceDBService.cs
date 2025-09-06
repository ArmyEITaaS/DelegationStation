using Azure.Core;
using DelegationStation.Interfaces;
using DelegationStationShared.Models;
using Microsoft.Azure.Cosmos;
using Azure.Identity;
using DelegationStationShared.Enums;

namespace DelegationStation.Services
{

    public class DeviceDBService : IDeviceDBService
    {
        private readonly ILogger<DeviceDBService> _logger;
        private readonly Container _container;
        private string? _DefaultGroup;

        public DeviceDBService(IConfiguration configuration, ILogger<DeviceDBService> logger)
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

        public async Task<Device?> GetDeviceAsync(string make, string model, string serialNumber)
        {
            List<Device> devices = new List<Device>();
            QueryDefinition q = new QueryDefinition("SELECT * FROM d WHERE d.Type = \"Device\" AND d.SerialNumber = @serial AND d.Make = @make AND d.Model = @model");
            q.WithParameter("@serial", serialNumber);
            q.WithParameter("@make", make);
            q.WithParameter("@model", model);

            var deviceQueryIterator = this._container.GetItemQueryIterator<Device>(q);
            while (deviceQueryIterator.HasMoreResults)
            {
                var qIresponse = await deviceQueryIterator.ReadNextAsync();
                devices.AddRange(qIresponse.ToList());
            }
            if (devices.Count == 0)
            {
                throw new Exception($"Device not found.");
            }
            else
            {
                return devices.FirstOrDefault();
            }
        }

        public async Task<List<Device>> GetDevicesSearchAsync(string make, string model, string serialNumber, int? osID, string preferredHostname)
        {
            List<Device> devices = new List<Device>();
            string queryBuilder = "SELECT * FROM d WHERE d.Type = \"Device\" ";
            if (!string.IsNullOrEmpty(make.Trim()))
            {
                queryBuilder += "AND CONTAINS(d.Make, @make, true) ";
            }

            if (!string.IsNullOrEmpty(model.Trim()))
            {
                queryBuilder += " AND CONTAINS(d.Model, @model, true)";
            }

            if (!string.IsNullOrEmpty(serialNumber.Trim()))
            {
                queryBuilder += " AND CONTAINS(d.SerialNumber, @serial, true)";
            }

            if (osID != null)
            {
                queryBuilder += " AND d.OS=@os";
            }
            if (!string.IsNullOrEmpty(preferredHostname.Trim()))
            {
                queryBuilder += " AND CONTAINS(d.PreferredHostname, @hostname, true)";
            }

            QueryDefinition q = new QueryDefinition(queryBuilder);

            q.WithParameter("@make", make);
            q.WithParameter("@model", model);
            q.WithParameter("@serial", serialNumber);
            q.WithParameter("@os", osID);
            q.WithParameter("@hostname", preferredHostname);

            var deviceQueryIterator = this._container.GetItemQueryIterator<Device>(q);
            while (deviceQueryIterator.HasMoreResults)
            {
                var qIresponse = await deviceQueryIterator.ReadNextAsync();
                devices.AddRange(qIresponse.ToList());
            }

            return devices;
        }

        public async Task<List<Device>> GetDevicesByTagAsync(string tagId)
        {
            List<Device> devices = new List<Device>();

            List<DeviceTag> deviceTags = new List<DeviceTag>();

            string query = "SELECT * FROM d WHERE d.Type = \"Device\" AND ARRAY_CONTAINS(d.Tags, @tagId, true)";

            QueryDefinition q = new QueryDefinition(query);
            q.WithParameter($"@tagId", tagId);

            var deviceQueryIterator = this._container.GetItemQueryIterator<Device>(q);
            while (deviceQueryIterator.HasMoreResults)
            {
                var response = await deviceQueryIterator.ReadNextAsync();
                devices.AddRange(response.ToList());
            }

            return devices;
        }

        public async Task<List<Device>> GetDevicesAsync(IEnumerable<string> groupIds)
        {
            List<Device> devices = new List<Device>();

            List<DeviceTag> deviceTags = new List<DeviceTag>();
            groupIds = groupIds.Where(g => System.Text.RegularExpressions.Regex.Match(g, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success);

            if (groupIds.Count() < 1)
            {
                throw new Exception("DeviceDBService GetDevicesAsync no valid group ids sent.");
            }

            // Get tags that the user has access to
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int argCount = 0;

            if (groupIds.Contains(_DefaultGroup))
            {
                sb.Append("SELECT * FROM t WHERE t.PartitionKey = \"DeviceTag\"");
            }
            else
            {
                sb.Append("SELECT t.id,t.Name,t.Description,t.RoleDelegations,t.UpdateActions,t.PartitionKey,t.Type FROM t JOIN r IN t.RoleDelegations WHERE t.PartitionKey = \"DeviceTag\" AND (");

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


            // Get Devices with the tags the user has access to
            sb = new System.Text.StringBuilder();
            argCount = 0;

            if (groupIds.Contains(_DefaultGroup))
            {
                sb.Append("SELECT * FROM d WHERE d.Type = \"Device\"");
            }
            else
            {
                sb.Append("SELECT * FROM d WHERE d.Type = \"Device\" AND (");

                foreach (DeviceTag tag in deviceTags)
                {
                    sb.Append($"ARRAY_CONTAINS(d.Tags, @arg{argCount}, true) ");
                    if (tag != deviceTags.Last())
                    {
                        sb.Append("OR ");
                    }
                    argCount++;
                }
                sb.Append(")");
            }


            argCount = 0;
            q = new QueryDefinition(sb.ToString());

            if (!groupIds.Contains(_DefaultGroup))
            {
                foreach (DeviceTag tag in deviceTags)
                {
                    q.WithParameter($"@arg{argCount}", tag.Id);
                    argCount++;
                }
            }

            var deviceQueryIterator = this._container.GetItemQueryIterator<Device>(q);
            while (deviceQueryIterator.HasMoreResults)
            {
                var response = await deviceQueryIterator.ReadNextAsync();
                devices.AddRange(response.ToList());
            }

            return devices;
        }

        public async Task<Device> AddOrUpdateDeviceAsync(Device device)
        {
            if (device == null)
            {
                throw new Exception("DeviceDBService AddOrUpdateDeviceAsync was sent null device");
            }

            // Scrubbing whitespace before entry
            device.Make = device.Make.Trim();
            device.Model = device.Model.Trim();
            device.SerialNumber = device.SerialNumber.Trim();

            // Confirm DB does not already contain device - treating fields as case insensitive
            List<Device> devices = new List<Device>();
            QueryDefinition q = new QueryDefinition("SELECT * FROM d WHERE d.Type = \"Device\" AND STRINGEQUALS(d.Make,@make,true) AND STRINGEQUALS(d.Model,@model,true) AND STRINGEQUALS(d.SerialNumber,@serial,true)");
            q.WithParameter("@make", device.Make);
            q.WithParameter("@model", device.Model);
            q.WithParameter("@serial", device.SerialNumber);

            var deviceQueryIterator = this._container.GetItemQueryIterator<Device>(q);
            while (deviceQueryIterator.HasMoreResults)
            {
                var qIresponse = await deviceQueryIterator.ReadNextAsync();
                devices.AddRange(qIresponse.ToList());
            }
            if (devices.Count != 0)
            {
                throw new Exception("Device already exists.");
            }

            ItemResponse<Device> response = await this._container.UpsertItemAsync<Device>(device);
            return response;
        }

        public async Task<Device> GetDeviceAsync(string deviceId)
        {
            if (deviceId == null)
            {
                throw new Exception("DeviceDBService GetDeviceAsync was sent null deviceId");
            }

            if (!System.Text.RegularExpressions.Regex.Match(deviceId, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success)
            {
                throw new Exception($"DeviceDBService GetDeviceAsync deviceId did not match GUID format {deviceId}");
            }

            ItemResponse<Device> response = await this._container.ReadItemAsync<Device>(deviceId, new PartitionKey(deviceId));
            return response;
        }

        public async Task<List<Device>> GetDevicesAsync(IEnumerable<string> groupIds, string search, int pageSize = 10, int page = 0)
        {
            List<Device> devices = new List<Device>();

            List<DeviceTag> deviceTags = new List<DeviceTag>();
            groupIds = groupIds.Where(g => System.Text.RegularExpressions.Regex.Match(g, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success);

            if (groupIds.Count() < 1)
            {
                return devices;
            }

            // Get tags that the user has access to
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int argCount = 0;

            if (groupIds.Contains(_DefaultGroup))
            {
                sb.Append("SELECT * FROM t WHERE t.PartitionKey = \"DeviceTag\"");
            }
            else
            {
                sb.Append("SELECT t.id,t.Name,t.Description,t.RoleDelegations,t.UpdateActions,t.PartitionKey,t.Type FROM t JOIN r IN t.RoleDelegations WHERE t.PartitionKey = \"DeviceTag\" AND (");

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


            // Get Devices with the tags the user has access to
            sb = new System.Text.StringBuilder();
            argCount = 0;

            if (groupIds.Contains(_DefaultGroup))
            {
                sb.Append("SELECT * FROM d WHERE d.Type = \"Device\"");
            }
            else
            {
                sb.Append("SELECT * FROM d WHERE d.Type = \"Device\" AND (");

                foreach (DeviceTag tag in deviceTags)
                {
                    sb.Append($"ARRAY_CONTAINS(d.Tags, @arg{argCount}, true) ");
                    if (tag != deviceTags.Last())
                    {
                        sb.Append("OR ");
                    }
                    argCount++;
                }
                sb.Append(")");
            }
            sb.Append(" AND (CONTAINS(d.Make, @search, true) OR CONTAINS(d.Model, @search, true) OR CONTAINS(d.SerialNumber, @search, true)) ORDER BY d.ModifiedUTC DESC OFFSET @offset LIMIT @limit");


            argCount = 0;
            q = new QueryDefinition(sb.ToString());

            if (!groupIds.Contains(_DefaultGroup))
            {
                foreach (DeviceTag tag in deviceTags)
                {
                    q.WithParameter($"@arg{argCount}", tag.Id);
                    argCount++;
                }
            }
            q.WithParameter("@search", search);
            q.WithParameter("@offset", page * pageSize);
            q.WithParameter("@limit", pageSize);

            var deviceQueryIterator = this._container.GetItemQueryIterator<Device>(q);
            while (deviceQueryIterator.HasMoreResults)
            {
                var response = await deviceQueryIterator.ReadNextAsync();
                _logger.LogInformation($"Search query cost: {response.RequestCharge.ToString()}");
                devices.AddRange(response.ToList());
            }

            return devices;
        }

        public async Task MarkDeviceToDeleteAsync(Device device)
        {
            device.Status = DeviceStatus.Deleting;
            device.MarkedToDeleteUTC = DateTime.UtcNow;
            await this._container.UpsertItemAsync<Device>(device);
        }

    }
}
