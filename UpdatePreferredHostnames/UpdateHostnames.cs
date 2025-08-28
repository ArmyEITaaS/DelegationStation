using Azure.Identity;
using DelegationStationShared;
using DelegationStationShared.Extensions;
using DelegationStationShared.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Beta.Models.WindowsUpdates;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
//using UpdatePreferredHostnames.Models;
using static Azure.Core.HttpHeader;

namespace UpdatePreferredHostnames
{
    public class UpdateHostnames
    {
        private readonly ILogger _logger;
        private static Container? _container = null;
        private static GraphServiceClient? _graphClient = null;

        public UpdateHostnames(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<UpdateHostnames>();
        }

        internal async Task RunAsync()
        {
            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Preferred Hostname update and flagging Job starting....", fullMethodName);

            ConnectToCosmosDb();
            if (_container == null)
            {
                _logger.DSLogError("Failed to connect to Cosmos DB, exiting.", fullMethodName);
                Environment.Exit(1);
            }

            await ConnectToGraph();
            if (_graphClient == null)
            {
                _logger.DSLogError("Failed to connect to Graph, exiting.", fullMethodName);
                Environment.Exit(1);
            }
            int result = await UpdateHostnamesAsync();

            _logger.DSLogInformation($"Preferred Hostname update and flagging Job done:  Updated {result} devices.", fullMethodName);

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
        private async Task ConnectToGraph()
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Connecting to Graph...", fullMethodName);

            var azureCloud = Environment.GetEnvironmentVariable("AzureEnvironment");
            var graphEndpoint = Environment.GetEnvironmentVariable("GraphEndpoint");
            var options = new TokenCredentialOptions
            {
                AuthorityHost = azureCloud == "AzurePublicCloud" ? AzureAuthorityHosts.AzurePublicCloud : AzureAuthorityHosts.AzureGovernment
            };

            var scopes = new string[] { $"{graphEndpoint}.default" };
            string baseUrl = graphEndpoint + "v1.0";

            var certDN = Environment.GetEnvironmentVariable("CertificateDistinguishedName");

            if (!String.IsNullOrEmpty(certDN))
            {
                _logger.DSLogInformation("Using certificate authentication: ", fullMethodName);
                _logger.DSLogDebug("AzureCloud: " + azureCloud, fullMethodName);
                _logger.DSLogDebug("GraphEndpoint: " + graphEndpoint, fullMethodName);

                X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);
                _logger.DSLogInformation("Using certificate with Subject Name {0} for Graph service: " + certDN, fullMethodName);
                var certificate = store.Certificates.Cast<X509Certificate2>().FirstOrDefault(cert => cert.Subject.ToString() == certDN);

                var clientCertCredential = new ClientCertificateCredential(
                    Environment.GetEnvironmentVariable("TenantId"),
                    Environment.GetEnvironmentVariable("ClientId"),
                    certificate,
                    options
                );
                store.Close();
                _graphClient = new GraphServiceClient(clientCertCredential, scopes, baseUrl);
            }
            else
            {
                _logger.DSLogInformation("Using Client Secret for Graph service", fullMethodName);
                _logger.DSLogDebug("AzureCloud: " + azureCloud, fullMethodName);
                _logger.DSLogDebug("GraphEndpoint: " + graphEndpoint, fullMethodName);


                var clientSecretCredential = new ClientSecretCredential(
                    Environment.GetEnvironmentVariable("TenantId"),
                    Environment.GetEnvironmentVariable("ClientId"),
                    Environment.GetEnvironmentVariable("ClientSecret"),
                    options
                );

                _graphClient = new GraphServiceClient(clientSecretCredential, scopes, baseUrl);
            }
        }
       
        private async Task<int> UpdateHostnamesAsync()
        {
            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;
            int count = 0;
            List<string> PresentUnenrolledDevices = new List<string>();
            PresentUnenrolledDevices.Add("Tag, Make, Model, Serial Number, OS, Hostname, Action");
            //
            // Retrieve Devices and their Ids and Preferred Hostnames
            //
            Queue<Device> devicesToCheck = new Queue<Device>();

            try
            {
                QueryDefinition query = new QueryDefinition("SELECT c.id as id, c.PreferredHostname as PreferredHostname, lower(c.Make) as Make, lower(c.Model) as Model, lower(c.SerialNumber) as SerialNumber, c.Tags as Tags " +
                                                            "FROM c WHERE c.Type='Device' ");
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                // TOFIX????  if _container is null, it will be caught in try block
                var queryIterator = _container.GetItemQueryIterator<Device>(query);
#pragma warning restore CS8602 // Dereference of a possibly null reference.
                while (queryIterator.HasMoreResults)
                {
                    var response = queryIterator.ReadNextAsync().Result;

                    foreach (var device in response)
                    {
                        devicesToCheck.Enqueue(device);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to query Cosmos for devices: ", ex, fullMethodName);
                return count;
            }

            Queue<Device> devicesToUpdate = new Queue<Device>();            
            // For each device, check if the Hostname in Intune matches the PreferredHostname in Cosmos.
            while (devicesToCheck.Count > 0)
            {
                var device = devicesToCheck.Dequeue();
                string deviceHostname = null;
                try
                {

                    var deviceObj = await _graphClient.DeviceManagement.ManagedDevices.GetAsync((requestConfiguration) =>
                    {
                        requestConfiguration.QueryParameters.Filter = $"serialNumber eq '{device.SerialNumber}' and model eq '{device.Model}'";
                        requestConfiguration.QueryParameters.Select = new string[] { "ManagedDeviceName" };
                    });
                    if(deviceObj == null || deviceObj.Value == null || deviceObj.Value.Count == 0)
                    {
                        _logger.DSLogWarning($"Device with Model '{device.Model}' and SerialNumber '{device.SerialNumber}' not found in Intune.", fullMethodName);
                        PresentUnenrolledDevices.Add($"{device.Tags[0]}, {device.Make}, {device.Model}, {device.SerialNumber}, , , ");
                        continue;
                    }
                    deviceHostname = deviceObj.Value.FirstOrDefault().ManagedDeviceName ?? "";
                    if(string.Equals(device.PreferredHostname, deviceHostname))
                    {
                        _logger.DSLogInformation($"Device {device.Id} PreferredHostname '{device.PreferredHostname}' matches Intune Hostname '{deviceHostname}', no update needed.", fullMethodName);
                        continue;
                    }
                    device.PreferredHostname = deviceHostname ?? "";
                    devicesToUpdate.Enqueue(device);
                }
                catch (Exception ex)
                {
                    _logger.DSLogException("Failed to retrieve graph device ID using .\n", ex, fullMethodName);
                    PresentUnenrolledDevices.Add($"{device.Tags[0]}, {device.Make}, {device.Model}, {device.SerialNumber}, , , ");
                }
            }

            // Update the PreferredHostname in Cosmos if it does not match the Intune hostname.
            while (devicesToUpdate.Count > 0)
            {
                var device = devicesToUpdate.Dequeue();

                try
                {
                    PatchOperation patchOperation = PatchOperation.Replace("/PreferredHostname", device.PreferredHostname);
                    IReadOnlyList<PatchOperation> patchOperations = new List<PatchOperation>() { patchOperation };
                    PartitionKey partitionKey = new PartitionKey(device.Id.ToString());
                    await _container.PatchItemAsync<Device>(device.Id.ToString(), partitionKey, patchOperations);
                    count++;
                    _logger.DSLogInformation($"Updated device {device.Id} PreferredHostname to '{device.PreferredHostname}'", fullMethodName);
                }
                catch (Exception ex)
                {
                    _logger.DSLogException($"Failed to delete duplicate device from Delegation Station: {device.Id}", ex, fullMethodName);
                }                
            }
            _logger.DSLogInformation($"Updated {count} devices.", fullMethodName);
            _logger.DSLogInformation("The following devices were not enrolled in Intune and could not be updated:", fullMethodName);
            foreach (var device in PresentUnenrolledDevices)
            {
                _logger.DSLogInformation(device);
            }
            return count;
        }
    }
}
