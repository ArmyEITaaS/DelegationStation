using Azure.Core;
using Azure.Identity;
using DelegationStationShared;
using DelegationStationShared.Extensions;
using DelegationStationShared.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UpdateDevices.Interfaces;
using UpdateDevices.Models;

namespace UpdateDevices.Services
{
  internal class CosmosDbService : ICosmosDbService
  {
    private readonly ILogger<CosmosDbService> _logger;
    private readonly CosmosClient _cosmosClient;
    private readonly Container _container;

    public CosmosDbService(ILogger<CosmosDbService> logger)
    {
      string methodName = ExtensionHelper.GetMethodName();
      string className = this.GetType().Name;
      string fullMethodName = className + "." + methodName;

      _logger = logger;

      string containerName = Environment.GetEnvironmentVariable("COSMOS_CONTAINER_NAME", EnvironmentVariableTarget.Process);
      string databaseName = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME", EnvironmentVariableTarget.Process);
      var connectionString = Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING", EnvironmentVariableTarget.Process);
      //bool managedIdAuthEnabled = false;
      //bool.TryParse(Environment.GetEnvironmentVariable("CosmosManagedIdAuthEnabled", EnvironmentVariableTarget.Process), out managedIdAuthEnabled);
      string cosmosEndpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT", EnvironmentVariableTarget.Process);


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

    public async Task<Device> GetDevice(string make, string model, string serialNumber)
    {
      string methodName = ExtensionHelper.GetMethodName();
      string className = this.GetType().Name;
      string fullMethodName = className + "." + methodName;

      Device device = new Device();

      // Search CosmosDB for device with exact match on Make, Model, SerialNumber
      // This is a case insensitive search.
      QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.Type = \"Device\" AND STRINGEQUALS(c.Make, @manufacturer, true) AND STRINGEQUALS(c.Model,@model, true) AND STRINGEQUALS(c.SerialNumber,@serialNumber, true)")
          .WithParameter("@manufacturer", make.Trim())
          .WithParameter("@model", model.Trim())
          .WithParameter("@serialNumber", serialNumber.Trim());
      var queryIterator = _container.GetItemQueryIterator<Device>(query);

      List<Device> deviceResults = new List<Device>();
      try
      {

        while (queryIterator.HasMoreResults)
        {
          var response = await queryIterator.ReadNextAsync();
          deviceResults.AddRange(response.ToList());
        }
        device = deviceResults.FirstOrDefault();
      }
      catch (Exception ex)
      {
        _logger.DSLogException("Failure querying Cosmos DB for device '" + make + "' '" + model + "' '" + serialNumber + "'.\n", ex, fullMethodName);
      }

      return device;
    }

    public async Task<DeviceTag> GetDeviceTag(string tagId)
    {
      string methodName = ExtensionHelper.GetMethodName();
      string className = this.GetType().Name;
      string fullMethodName = className + "." + methodName;

      DeviceTag tag = null; //= new DeviceTag();

      try
      {
        ItemResponse<DeviceTag> tagResponse = await _container.ReadItemAsync<DeviceTag>(tagId, new PartitionKey("DeviceTag"));
        tag = tagResponse.Resource;
      }
      catch (Exception ex)
      {
        _logger.DSLogException("Get tag " + tagId + " failed. ", ex, fullMethodName);
      }

      return tag;
    }

    public async Task<FunctionSettings> GetFunctionSettings()
    {
      string methodName = ExtensionHelper.GetMethodName();
      string className = this.GetType().Name;
      string fullMethodName = className + "." + methodName;
      FunctionSettings settings = new FunctionSettings();
      try
      {
        settings = await _container.ReadItemAsync<FunctionSettings>(settings.Id.ToString(), new PartitionKey(settings.PartitionKey));
        _logger.DSLogInformation("Successfully retrieved function settings:  " + settings.ToString(), fullMethodName);
      }
      catch (Exception ex)
      {
        _logger.DSLogException("Unable to retrieve function settings.", ex, fullMethodName);
      }
      finally
      {
        if (settings == null)
        {
          settings = new FunctionSettings();
        }
      }

      return settings;
    }

    public async Task UpdateFunctionSettings(DateTime thisRun)
    {
      string methodName = ExtensionHelper.GetMethodName();
      string className = this.GetType().Name;
      string fullMethodName = className + "." + methodName;

      FunctionSettings settings = new FunctionSettings();
      settings.LastRun = thisRun;
      try
      {
        var response = await _container.UpsertItemAsync<FunctionSettings>(settings, new PartitionKey(settings.PartitionKey));
        _logger.DSLogInformation("Successfully updated function settings:  " + settings.ToString(), fullMethodName);
      }
      catch (Exception ex)
      {
        _logger.DSLogException("Unable to update function settings.", ex, fullMethodName);
      }
    }

        public async Task<Straggler> GetStraggler(string managedDeviceID)
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            Straggler straggler = null;

            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.PartitionKey= \"Straggler\" AND c.ManagedDeviceID = @managedDeviceID")
                .WithParameter("@managedDeviceID", managedDeviceID);
            var queryIterator = _container.GetItemQueryIterator<Straggler>(query);

            List<Straggler> results = new List<Straggler>();
            try
            {
                while (queryIterator.HasMoreResults)
                {
                    var response = await queryIterator.ReadNextAsync();
                    results.AddRange(response.ToList());
                }
                straggler = results.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failure querying Cosmos DB for Straggler (ManagedDeviceID " + managedDeviceID + "):\n", ex, fullMethodName);
            }

            return straggler;
        }

        // This method is intended specifically for use by UpdateDevices
           public async Task AddOrUpdateStraggler(Microsoft.Graph.Models.ManagedDevice managedDevice)
        {
            Straggler straggler = await GetStraggler(managedDevice.Id);
            if (straggler == null)
            {
                straggler = new Straggler();
                straggler.ManagedDeviceID = managedDevice.Id;
                straggler.EnrollmentDateTime = managedDevice.EnrolledDateTime.Value.UtcDateTime;
                straggler.LastUDUpdateDateTime = DateTime.UtcNow;
                straggler.LastSeenDateTime = DateTime.UtcNow;
            }
            else
            {
                straggler.UDAttemptCount++;
                straggler.LastUDUpdateDateTime = DateTime.UtcNow;
                straggler.LastSeenDateTime = DateTime.UtcNow;
            }

            await _container.UpsertItemAsync<Straggler>(straggler, new PartitionKey(straggler.PartitionKey));
        }

        // This method is intended specifically for use by the StragglerHandler
        public async Task UpdateStraggler(Straggler straggler)
        {
            straggler.LastSeenDateTime = DateTime.UtcNow;
            straggler.LastSHAttemptDateTime = DateTime.UtcNow;
            straggler.SHAttemptCount++;
            await _container.UpsertItemAsync<Straggler>(straggler, new PartitionKey(straggler.PartitionKey));
        }

        public async Task UpdateStragglerAsErrored(Straggler straggler)
        {
            straggler.SHErrorCount++;
            straggler.LastSeenDateTime = DateTime.UtcNow;
            straggler.LastSHAttemptDateTime = DateTime.UtcNow;
            await _container.UpsertItemAsync<Straggler>(straggler, new PartitionKey(straggler.PartitionKey));
        }

        public async Task DeleteStraggler(Straggler straggler)
        {
            await _container.DeleteItemAsync<Straggler>(straggler.id.ToString(), new PartitionKey(straggler.PartitionKey));
        }

        public async Task<List<Straggler>> GetStragglerList(int minCount)
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.PartitionKey= \"Straggler\" AND c.UDAttemptCount>=@count")
                .WithParameter("@count", minCount);
            var queryIterator = _container.GetItemQueryIterator<Straggler>(query);

            List<Straggler> results = new List<Straggler>();
            try
            {
                while (queryIterator.HasMoreResults)
                {
                    var response = await queryIterator.ReadNextAsync();
                    results.AddRange(response.ToList());
                }
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failure querying Cosmos DB for Straggler: ", ex, fullMethodName);
            }

            return results;
        }


        // Removes entries that were enrolled over a day ago and have less than the max number
        // retries indicating UpdateDevices was able to process them before it stopped retrying
        public async Task<List<Straggler>> GetStragglersProcessedByUD(int minCount)
        {
            string methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            DateTime aDayAgo = DateTime.UtcNow.AddDays(-1);

            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.PartitionKey= \"Straggler\" AND c.UDAttemptCount < @count AND c.EnrollmentDateTime < @oneDayAgo")
                .WithParameter("@count", minCount)
                .WithParameter("@oneDayAgo", aDayAgo);
            var queryIterator = _container.GetItemQueryIterator<Straggler>(query);

            List<Straggler> results = new List<Straggler>();
            try
            {
                while (queryIterator.HasMoreResults)
                {
                    var response = await queryIterator.ReadNextAsync();
                    results.AddRange(response.ToList());
                }
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failure querying Cosmos DB for Straggler: ", ex, fullMethodName);
            }

            return results;
        }

    }
}
