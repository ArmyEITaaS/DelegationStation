using Azure.Core;
using Azure.Identity;
using DelegationStationShared;
using DelegationStationShared.Extensions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System.Security.Cryptography.X509Certificates;
using Device = DelegationStationShared.Models.Device;
using Newtonsoft.Json.Linq;


namespace DeviceAudit
{
    public class DeviceAudit
    {
        private readonly ILogger _logger;
        private static Container? _container = null;
        private static GraphServiceClient? _graphClient = null;

        public DeviceAudit(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<DeviceAudit>();
        }

        internal async Task RunAsync()
        {
            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Device Audit Job starting....", fullMethodName);

            ConnectToCosmosDb();
            if (_container == null)
            {
                _logger.DSLogError("Failed to connect to Cosmos DB, exiting.", fullMethodName);
                Environment.Exit(1);
            }

            ConnectToGraph();
            if (_graphClient == null)
            {
                _logger.DSLogError("Failed to connect to Graph, exiting.", fullMethodName);
                Environment.Exit(1);
            }

            await RunAuditAsync();

            _logger.DSLogInformation("Device Audit Job done.", fullMethodName);
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

        private void ConnectToGraph()
        {
            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Connecting to Microsoft Graph...", fullMethodName);

            string? tenantId = Environment.GetEnvironmentVariable("AzureAd__TenantId");
            string? clientId = Environment.GetEnvironmentVariable("AzureAd__ClientId");
            string? clientSecret = Environment.GetEnvironmentVariable("AzureApp__ClientSecret");
            string? certDN = Environment.GetEnvironmentVariable("AzureAd__ClientCertificates__CertificateDistinguishedName");
            string graphEndpoint = Environment.GetEnvironmentVariable("GraphEndpoint") ?? "https://graph.microsoft.com/";
            string? azureEnvironment = Environment.GetEnvironmentVariable("AzureEnvironment");

            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId))
            {
                _logger.DSLogError("Missing one or more required Graph environment variables: AzureAd__TenantId, AzureAd__ClientId.", fullMethodName);
                return;
            }

            if (string.IsNullOrEmpty(clientSecret) && string.IsNullOrEmpty(certDN))
            {
                _logger.DSLogError("Missing authentication credential. Set either AzureApp__ClientSecret or AzureAd__ClientCertificates__CertificateDistinguishedName.", fullMethodName);
                return;
            }

            try
            {
                var options = new TokenCredentialOptions
                {
                    AuthorityHost = azureEnvironment == "AzurePublicCloud"
                        ? AzureAuthorityHosts.AzurePublicCloud
                        : AzureAuthorityHosts.AzureGovernment
                };

                var scopes = new string[] { $"{graphEndpoint}.default" };
                string baseUrl = graphEndpoint + "v1.0";

                if (!string.IsNullOrEmpty(certDN))
                {
                    _logger.DSLogInformation("Using certificate authentication for Graph.", fullMethodName);
                    X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                    store.Open(OpenFlags.ReadOnly);
                    _logger.DSLogInformation($"Using certificate with Subject Name {certDN} for Graph service.", fullMethodName);
                    var certificate = store.Certificates.Cast<X509Certificate2>().FirstOrDefault(cert => cert.Subject == certDN);
                    store.Close();

                    var clientCertCredential = new ClientCertificateCredential(tenantId, clientId, certificate, options);
                    _graphClient = new GraphServiceClient(clientCertCredential, scopes, baseUrl);
                }
                else
                {
                    _logger.DSLogInformation("Using Client Secret for Graph service.", fullMethodName);
                    var clientSecretCredential = new ClientSecretCredential(tenantId, clientId, clientSecret, options);
                    _graphClient = new GraphServiceClient(clientSecretCredential, scopes, baseUrl);
                }

                _logger.DSLogInformation("Connected to Microsoft Graph.", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to connect to Graph: ", ex, fullMethodName);
            }
        }

        private async Task RunAuditAsync()
        {
            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            bool testMode = Environment.GetEnvironmentVariable("AUDIT_TEST_MODE") == "true" ? true : false;

            // if present pull in list of tags we can skip
            List<string> skippableTags = new List<string>();
            string? tagsToSkip = Environment.GetEnvironmentVariable("AUDIT_TAGS_TO_SKIP");
            if(!string.IsNullOrEmpty(tagsToSkip))
            {
                _logger.DSLogInformation($"Detecting configuration to skip some tags:  {tagsToSkip}", fullMethodName);
                skippableTags = tagsToSkip.Split(',').Where(item => !string.IsNullOrEmpty(item)).ToList();
            }

            // Setup directory for output file
            string outputDir = "D:\\home\\device_audit_outputs";
            if(!Directory.Exists(outputDir))
            {
                _logger.DSLogInformation($"Output path does not exist.  Creating {outputDir}", fullMethodName);
                Directory.CreateDirectory(outputDir);
            }
            string fileName = $"DeviceAudit_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            string filePath = Path.Combine(outputDir, fileName);
            _logger.DSLogInformation($"Audit results will be written to: {filePath}", fullMethodName);


            List<string> uniqueIDs = await GetUniqueAddedBys();

            // Get all the unique UserIDs in the AddedBy field and their UPNs to minimize Graph calls
            Dictionary<string,string> userMapping = await GetUserUPNMappingsAsync(uniqueIDs);

            // Get tags and IDs to translate tag IDs to names in output
            Dictionary<string, string> tagMapping = await GetTagMappings();



            await using var writer = new StreamWriter(filePath);
            writer.AutoFlush = true;
            await writer.WriteLineAsync("Tag,Make,Model,SerialNumber,Date Added to DS,AddedBy,AddedBy UPN,Found in Intune,How Many,Intune Device IDs");


            foreach (var tag_kvp in tagMapping)
            {
                var tagID = tag_kvp.Key;
                var tagName = tag_kvp.Value;

                if (!skippableTags.Contains(tagName))
                {

                    List<Device> devices = await GetDevicesInTag(tagID, testMode);
                    _logger.DSLogInformation($"Processing {devices.Count} devices for tag {tagName} ({tagID}).", fullMethodName);

                    foreach (var device in devices)
                    {
                        List<ManagedDevice> intuneMatches = await GetIntuneDevicesAsync(device.Make, device.Model, device.SerialNumber);
                        bool foundInIntune = intuneMatches.Count > 0;
                        int matchCount = intuneMatches.Count;
                        string intuneIds = string.Join("|", intuneMatches.Select(m => m.Id ?? string.Empty));

                        string upn = userMapping[device.AddedBy ?? string.Empty];

                        string line = string.Join(",",
                            CsvEscape(tagName),
                            CsvEscape(device.Make),
                            CsvEscape(device.Model),
                            CsvEscape(device.SerialNumber),
                            CsvEscape(device.ModifiedUTC.ToString("MM/dd/yyyy HH:mm:ss")),
                            CsvEscape(device.AddedBy ?? string.Empty),
                            CsvEscape(upn),
                            foundInIntune ? "Yes" : "No",
                            matchCount.ToString(),
                            CsvEscape(intuneIds)
                        );
                        await writer.WriteLineAsync(line);

                    }
                    _logger.DSLogInformation($"Completed auditing tag: {tagName}", fullMethodName);
                }
                else
                {
                    _logger.DSLogInformation($"Skipped tag:  {tagName}", fullMethodName);
                }

            }

            _logger.DSLogInformation($"Audit complete.", fullMethodName);
        }

        private async Task<List<Device>> GetDevicesInTag(string tagID, bool testMode)
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
                QueryDefinition query;
                if (testMode)
                {
                    _logger.DSLogInformation($"Test mode enabled - limiting query to 10 results.", fullMethodName);
                    query = new QueryDefinition("SELECT * FROM c WHERE c.Type = 'Device' AND ARRAY_CONTAINS(c.Tags, @tagID) OFFSET 0 LIMIT 10");
                }
                else
                {
                    query = new QueryDefinition("SELECT * FROM c WHERE c.Type = 'Device' AND ARRAY_CONTAINS(c.Tags, @tagID)");
                }
                query.WithParameter("@tagID", tagID);
                var iterator = _container.GetItemQueryIterator<Device>(query);

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    devices.AddRange(response);
                }

                _logger.DSLogInformation($"Retrieved {devices.Count} devices from Cosmos DB.", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to query devices from Cosmos DB: ", ex, fullMethodName);
            }

            return devices;
        }

        private async Task<List<string>> GetUniqueAddedBys()
            {
            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            var userIDs = new List<string>();

            if (_container == null)
            {
                _logger.DSLogError("Cosmos container is null.", fullMethodName);
                return userIDs;
            }

            try
            {
                var query = new QueryDefinition("SELECT DISTINCT VALUE c.AddedBy FROM c WHERE c.Type = 'Device' AND IS_DEFINED(c.AddedBy) AND c.AddedBy != null");
                var iterator = _container.GetItemQueryIterator<string>(query);

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    userIDs.AddRange(response);
                }

                _logger.DSLogInformation($"Retrieved {userIDs.Count} UserIDs from Cosmos DB.", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to query devices from Cosmos DB: ", ex, fullMethodName);
            }

            return userIDs;

        }

        private async Task<Dictionary<string,string>> GetTagMappings()
        {
            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            var tagMappings = new Dictionary<string, string>();

            if (_container == null)
            {
                _logger.DSLogError("Cosmos container is null.", fullMethodName);
                return tagMappings;
            }

            try
            {
                var query = new QueryDefinition("SELECT c.id, c.Name FROM c WHERE c.PartitionKey='DeviceTag'");
                var iterator = _container.GetItemQueryIterator<JObject>(query);
                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    foreach (JObject item in response)
                    {
                        string id = item["id"]?.ToString() ?? string.Empty;
                        string name = item["Name"]?.ToString() ?? string.Empty;
                        tagMappings.Add(id, name);
                    }
                }
                _logger.DSLogInformation($"Retrieved {tagMappings.Count} tag mappings from Cosmos DB.", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to query tag mappings from Cosmos DB: ", ex, fullMethodName);
            }
            return tagMappings;
        }



        private async Task<List<ManagedDevice>> GetIntuneDevicesAsync(string make, string model, string serialNumber)
        {
            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            var results = new List<ManagedDevice>();

            if (_graphClient == null)
            {
                return results;
            }

            try
            {

                var response = await _graphClient.DeviceManagement.ManagedDevices.GetAsync((requestConfiguration) =>
                {
                    requestConfiguration.QueryParameters.Filter =
                        $"manufacturer eq '{make}' and model eq '{model}' and serialNumber eq '{serialNumber}'";
                });

                if (response?.Value != null)
                {
                    var pageIterator = PageIterator<ManagedDevice, ManagedDeviceCollectionResponse>
                        .CreatePageIterator(_graphClient, response, (device) =>
                        {
                            results.Add(device);
                            return true;
                        });

                    await pageIterator.IterateAsync();
                }

                _logger.DSLogInformation($"Found {results.Count} Intune match(es) for: {make} / {model} / {serialNumber}", fullMethodName);
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError err)
            {
                _logger.DSLogWarning($"Graph OData error querying Intune for {make}/{model}/{serialNumber}: {err.Error?.Message}", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException($"Failed to query Intune for {make}/{model}/{serialNumber}: ", ex, fullMethodName);
            }

            return results;
        }


        private async Task<Dictionary<string, string>> GetUserUPNMappingsAsync(List<string> usersIds)
        {
            string? methodName = ExtensionHelper.GetMethodName();
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            Dictionary<string, string> userCache = new Dictionary<string, string>();

            foreach (var userId in usersIds)
            {
                string userUPN = "";
                try
                {
                  var user = await _graphClient.Users[userId].GetAsync((requestConfiguration) =>
                    {
                        requestConfiguration.QueryParameters.Select = new string[] { "userPrincipalName" };
                    });
                    userUPN = user?.UserPrincipalName ?? string.Empty;
                    _logger.DSLogInformation($"Resolved UPN for user {userId}: {userUPN}", fullMethodName);
                    userCache.Add(userId, userUPN);

                }
                catch (Microsoft.Graph.Models.ODataErrors.ODataError err) when (err.ResponseStatusCode == 404)
                {
                    _logger.DSLogWarning($"User {userId} not found in Graph.", fullMethodName);
                    userCache.Add(userId, "UserID Not Found");
                }
                catch (Exception ex)
                {
                    _logger.DSLogException($"Failed to look up UPN for user {userId}: ", ex, fullMethodName);
                    userCache.Add(userId, "Error looking up UPN");
                }
            }

            return userCache;
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
