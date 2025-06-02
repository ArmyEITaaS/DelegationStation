using DelegationStationShared;
using DelegationStationShared.Extensions;
using DelegationStationShared.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using RemoveCaseSensitiveDuplicates.Models;

namespace RemoveCaseSensitiveDuplicates
{
    public class RemoveDupes
    {
        private readonly ILogger _logger;
        private static Container? _container = null;

        public RemoveDupes(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<RemoveDupes>();
        }

        internal async Task RunAsync()
        {
            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Case-Insensitive Duplicate Removal Job starting....", fullMethodName);

            ConnectToCosmosDb();
            if (_container == null)
            {
                _logger.DSLogError("Failed to connect to Cosmos DB, exiting.", fullMethodName);
                Environment.Exit(1);
            }
            int result = await CleanupAsync();

            _logger.DSLogInformation($"Case-Insensitive Duplicate Remove Job done:  Removed {result} duplicate devices.", fullMethodName);

        }

        private void ConnectToCosmosDb()
        {

            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Connecting to Cosmos DB...", fullMethodName);

            string? containerName = Environment.GetEnvironmentVariable("COSMOS_CONTAINER_NAME");
            string? databaseName = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME");
            var connectionString = Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING");

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
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.DSLogError("Cannot connect to CosmosDB. Missing required environment variable COSMOS_CONNECTION_STRING", fullMethodName);
                return;
            }

            try
            {
                CosmosClient client = new(connectionString: connectionString);
                _container = client.GetContainer(databaseName, containerName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to connect to CosmosDB: ", ex, fullMethodName);
                return;
            }

            _logger.DSLogInformation($"Connected to Cosmos DB database {databaseName} container {containerName}.", fullMethodName);
        }

        private async Task<int> CleanupAsync()
        {
            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            int count = 0;

            bool deleteFlag = false;
            bool.TryParse(Environment.GetEnvironmentVariable("DELETE_DUPLICATES"),out deleteFlag);
            if (!deleteFlag)
            {
                _logger.DSLogInformation("DELETE_DUPLICATES is not set or not set to true.  This run will only output what would be deleted.", fullMethodName);
            }
            else
            {
                _logger.DSLogInformation("DELETE_DUPLICATES is set to true.  This run will delete duplicates.", fullMethodName);
            }

            //
            // Retrieve counts for unique M/M/SN/Tag combinations
            //
            List<Duplicate> dupesToCleanup = new List<Duplicate>();
            try
            {
                QueryDefinition query = new QueryDefinition("SELECT lower(c.Make) as Make, lower(c.Model) as Model, lower(c.SerialNumber) as SerialNumber, c.Tags[0] as Tag0, count(c._ts) as Count " +
                                                            "FROM c WHERE c.Type='Device' " +
                                                            "GROUP BY lower(c.Make), lower(c.Model), lower(c.SerialNumber), c.Tags[0]");
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                // TOFIX????  if _container is null, it will be caught in try block
                var queryIterator = _container.GetItemQueryIterator<Duplicate>(query);
#pragma warning restore CS8602 // Dereference of a possibly null reference.

                while (queryIterator.HasMoreResults)
                {
                    var response = queryIterator.ReadNextAsync().Result;

                    foreach (var duplicate in response)
                    {
                        if (duplicate.Count > 1)
                        {
                            _logger.DSLogInformation($"Found {duplicate.Count} duplicates when case ignored: '{duplicate.Make}' '{duplicate.Model}' '{duplicate.SerialNumber}' Tag ID: {duplicate.Tag0}", fullMethodName);
                            dupesToCleanup.Add(duplicate);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Exiting.  Failed to query CosmosDB for duplicates: ", ex, fullMethodName);
                return count;
            }


            //
            // For each of the unique combos, get all devices sorted newest to oldest
            // we'll keep the newest and add the rest to a list to delete
            //
            List<Device> devicesToDelete = new List<Device>();
            foreach (var dupe in dupesToCleanup)
            {
                _logger.DSLogInformation($"Processing devices matching '{dupe.Make}' '{dupe.Model}' '{dupe.SerialNumber}' Tag: {dupe.Tag0}", fullMethodName);
                try
                {
                    QueryDefinition query2 = new QueryDefinition("SELECT * FROM  c WHERE c.Type='Device' AND STRINGEQUALS(c.Make,@make,true) AND STRINGEQUALS(c.Model,@model,true) AND STRINGEQUALS(c.SerialNumber,@serialNumber,true) " +
                                                                 "AND c.Tags[0]=@tag ORDER BY c.ModifiedUTC ASC");
                    query2.WithParameter("@make", dupe.Make);
                    query2.WithParameter("@model", dupe.Model);
                    query2.WithParameter("@serialNumber", dupe.SerialNumber);
                    query2.WithParameter("@tag", dupe.Tag0);

                    var queryIterator2 = _container.GetItemQueryIterator<Device>(query2);

                    bool savedOffFirstResult = false;
                    while (queryIterator2.HasMoreResults)
                    {
                        var response = queryIterator2.ReadNextAsync().Result;

                        foreach (var device in response)
                        {
                            if (savedOffFirstResult)
                            {
                                devicesToDelete.Add(device);
                                _logger.DSLogInformation($"   Marking duplicate for deletion: '{device.Make}' '{device.Model}' '{device.SerialNumber}' Tag: {device.Tags[0]}", fullMethodName);
                            }
                            else
                            {
                                _logger.DSLogInformation($"   Keeping newest entry: '{device.Make}' '{device.Model}' '{device.SerialNumber}' Tag: {device.Tags[0]}", fullMethodName);
                                savedOffFirstResult = true;
                            }

                        }

                    }
                }
                catch (Exception ex)
                {
                    _logger.DSLogException("Exiting. Failed to query CosmosDB for duplicate device details: ", ex, fullMethodName);
                    return count;
                }
            }
            _logger.DSLogInformation($"Found {devicesToDelete.Count} duplicate devices to delete.", fullMethodName);

            //
            // Delete all devices marked for deletion
            //
            foreach (var device in devicesToDelete)
            {
                try
                {
                    if (deleteFlag)
                    {
                        await _container.DeleteItemAsync<Device>(device.Id.ToString(), new PartitionKey(device.PartitionKey));
                    }
                    count++;
                }
                catch (Exception ex)
                {
                    _logger.DSLogException($"Failed to delete duplicate device from Delegation Station: {device.Id}", ex, fullMethodName);
                }
            }
            _logger.DSLogInformation($"Deleted {count} devices.", fullMethodName);



            //
            // Check for M/M/SN with dupes across tags - we're just going to log these
            //
            _logger.DSLogInformation("Checking for duplicate across tags....");
            int devicesToReview = 0;

            try
            {
                QueryDefinition query3 = new QueryDefinition("SELECT lower(c.Make) as Make, lower(c.Model) as Model, lower(c.SerialNumber) as SerialNumber, count(c._ts) as Count " +
                                                            "FROM c WHERE c.Type='Device' " +
                                                            "GROUP BY lower(c.Make), lower(c.Model), lower(c.SerialNumber)");
                var queryIterator3 = _container.GetItemQueryIterator<Duplicate>(query3);

                while (queryIterator3.HasMoreResults)
                {
                    var response = queryIterator3.ReadNextAsync().Result;

                    foreach (var duplicate in response)
                    {
                        if (duplicate.Count > 1)
                        {
                            _logger.DSLogWarning($"Found {duplicate.Count} duplicates across tags when case ignored: '{duplicate.Make}' '{duplicate.Model}' '{duplicate.SerialNumber}'", fullMethodName);

                            try
                            {
                                QueryDefinition query4 = new QueryDefinition("SELECT * FROM  c WHERE c.Type='Device' AND STRINGEQUALS(c.Make,@make,true) AND STRINGEQUALS(c.Model,@model,true) AND " +
                                                                             "STRINGEQUALS(c.SerialNumber,@serialNumber,true) ORDER BY c.ModifiedUTC DESC");
                                query4.WithParameter("@make", duplicate.Make);
                                query4.WithParameter("@model", duplicate.Model);
                                query4.WithParameter("@serialNumber", duplicate.SerialNumber);

                                var queryIterator4 = _container.GetItemQueryIterator<Device>(query4);
                                while (queryIterator4.HasMoreResults)
                                {
                                    var response4 = queryIterator4.ReadNextAsync().Result;
                                    foreach (var device in response4)
                                    {
                                        _logger.DSLogWarning($"     Individual device details: (CosmosID) {device.Id} '{device.Make}' '{device.Model}' '{device.SerialNumber}' Tag: {device.Tags[0]}", fullMethodName);
                                        devicesToReview++;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.DSLogException("Failed to query CosmosDB for duplicate device details: ", ex, fullMethodName);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to query CosmosDB for duplicates across tags: ", ex, fullMethodName);
            }

            _logger.DSLogWarning($"Found {devicesToReview} duplicate devices to review.", fullMethodName);


            return count;
        }
    }
}
