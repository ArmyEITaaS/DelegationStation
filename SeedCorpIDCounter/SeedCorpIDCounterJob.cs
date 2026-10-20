using Azure.Core;
using Azure.Identity;
using DelegationStationShared;
using DelegationStationShared.Extensions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using DelegationStationShared.Models;

namespace SeedCorpIDCounter
{
    public class SeedCorpIDCounterJob
    {
        private readonly ILogger _logger;
        private Container? _container = null;

        public SeedCorpIDCounterJob(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<SeedCorpIDCounterJob>();
        }

        internal async Task RunAsync(int corpIdCount)
        {
            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Seed CorpIDCounter Job starting...", fullMethodName);

            ConnectToCosmosDb();
            if (_container == null)
            {
                _logger.DSLogError("Failed to connect to Cosmos DB, exiting.", fullMethodName);
                Environment.Exit(1);
            }

            // Check if a CorpIDCounter already exists
            _logger.DSLogInformation("Checking for existing CorpIDCounter document...", fullMethodName);

            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.PartitionKey = \"CorpIDCounter\"");
            var queryIterator = _container.GetItemQueryIterator<CorpIDCounter>(query);

            while (queryIterator.HasMoreResults)
            {
                var response = await queryIterator.ReadNextAsync();
                var existing = response.FirstOrDefault();
                if (existing != null)
                {
                    _logger.DSLogError(
                        $"CorpIDCounter already exists (id: {existing.id}, CorpIDCount: {existing.CorpIDCount}, CorpIDReserve: {existing.CorpIDReserve}). " +
                        "Refusing to overwrite. Exiting.", fullMethodName);
                    Environment.Exit(1);
                    return;
                }
            }

            // Create the new CorpIDCounter document
            var counter = new CorpIDCounter(corpIdCount);

            _logger.DSLogInformation($"Creating CorpIDCounter with CorpIDCount = {corpIdCount}...", fullMethodName);

            try
            {
                await _container.CreateItemAsync(counter, new PartitionKey(counter.PartitionKey));
                _logger.DSLogInformation(
                    $"CorpIDCounter created successfully (id: {counter.id}, Counter: {counter.ToString()}).", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to create CorpIDCounter document.", ex, fullMethodName);
                Environment.Exit(1);
            }
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
    }
}
