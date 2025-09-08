using Azure.Core;
using Azure.Identity;
using DelegationStation.Interfaces;
using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.Azure.Cosmos;

namespace DelegationStation.Services
{
    public class RoleDBService : IRoleDBService
    {
        private readonly ILogger<DeviceDBService> _logger;
        private readonly Container _container;
        private string? _DefaultGroup;

        public RoleDBService(IConfiguration configuration, ILogger<DeviceDBService> logger)
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

        public async Task<List<Role>> GetRolesAsync()
        {
            List<Role> roles = new List<Role>();
            string query = $"SELECT * FROM r WHERE r.PartitionKey = \"{typeof(Role).Name}\"";

            QueryDefinition q = new QueryDefinition(query);

            var queryIterator = this._container.GetItemQueryIterator<Role>(q);
            while (queryIterator.HasMoreResults)
            {
                var response = await queryIterator.ReadNextAsync();
                roles.AddRange(response.ToList());
            }

            return roles;
        }

        public async Task<Role> AddOrUpdateRoleAsync(Role role)
        {
            if (role == null)
            {
                throw new Exception("RoleDBService AddOrUpdateRoleAsync was sent null role");
            }

            role.Attributes.Where(a => a == AllowedAttributes.All).ToList().ForEach(a => role.Attributes.Remove(a));
            ItemResponse<Role> response = await this._container.UpsertItemAsync<Role>(role);
            return response;
        }

        public async Task<Role> GetRoleAsync(string roleId)
        {
            if (roleId == null)
            {
                throw new Exception("RoleDBService GetRoleAsync was sent null roleId");
            }

            if (!System.Text.RegularExpressions.Regex.Match(roleId, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success)
            {
                throw new Exception($"DeviceDBService GetDeviceAsync deviceId did not match GUID format {roleId}");
            }

            ItemResponse<Role> response = await this._container.ReadItemAsync<Role>(roleId, new PartitionKey(typeof(Role).Name));
            return response;
        }

        public async Task DeleteRoleAsync(Role role)
        {
            ItemResponse<Role> response = await this._container.DeleteItemAsync<Role>(role.Id.ToString(), new PartitionKey(typeof(Role).Name));
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new Exception($"RoleDBService DeleteRoleAsync failed to delete role {role.Id}");
            }
        }
    }
}