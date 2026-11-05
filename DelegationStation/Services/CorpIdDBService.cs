using Azure.Core;
using Azure.Identity;
using DelegationStation.Interfaces;
using DelegationStationShared.Models;
using Microsoft.Azure.Cosmos;

namespace DelegationStation.Services
{
    public class CorpIdDBService : ICorpIdDBService
    {
        private readonly ILogger<CorpIdDBService> _logger;
        private readonly Container _container;

        public CorpIdDBService(IConfiguration configuration, ILogger<CorpIdDBService> logger)
        {
            _logger = logger;
            if (configuration == null)
            {
                throw new Exception("CorpIdDBService appsettings configuration is null.");
            }

            string cosmosEndpoint = configuration.GetSection("COSMOS_ENDPOINT").Value ?? "";
            string cosmosConnectionString = configuration.GetSection("COSMOS_CONNECTION_STRING").Value ?? "";

            if (string.IsNullOrEmpty(cosmosConnectionString) && string.IsNullOrEmpty(cosmosEndpoint))
            {
                throw new Exception("CorpIdDBService appsettings COSMOS_CONNECTION_STRING and COSMOS_ENDPOINT settings are both null or empty. At least one must be set.");
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
        }
        public async void ConfigureCosmosDatabase(CosmosClient client, string databaseName, string containerName)
        {
            DatabaseResponse database = await client.CreateDatabaseIfNotExistsAsync(databaseName);
            await database.Database.CreateContainerIfNotExistsAsync(containerName, "/PartitionKey");
        }
        public async Task<CorpIDCounter?> GetCorpIDCounterAsync()
        {
            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.PartitionKey = \"CorpIDCounter\"");
            var queryIterator = this._container.GetItemQueryIterator<CorpIDCounter>(query);
            while (queryIterator.HasMoreResults)
            {
                FeedResponse<CorpIDCounter> response = await queryIterator.ReadNextAsync();
                CorpIDCounter? counter = response.FirstOrDefault();
                if (counter != null)
                {
                    return counter;
                }
            }

            return null;
        }
    }
}
