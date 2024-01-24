using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.Cosmos;
using System.Text;
using Microsoft.Graph;
using System.Security.Cryptography.X509Certificates;
using Azure.Identity;
using System.Linq;
using UpdateDevices.Models;
using Microsoft.Graph.Models;
using System.Text.RegularExpressions;
using DelegationStationShared.Models;
using Microsoft.Azure.Functions.Worker;

namespace UpdateDevices
{
    public class UpdateDevices
    {
        private static string _guidRegex = "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$";
        private static Microsoft.Azure.Cosmos.Container _container = null;
        private GraphServiceClient _graphClient;

        private readonly ILogger _logger;

        public UpdateDevices(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<UpdateDevices>();
        }

        [Function("UpdateDevices")]
        public async Task Run([TimerTrigger("%TriggerTime%")] TimerInfo timerInfo)
        {
            MethodBase method = System.Reflection.MethodBase.GetCurrentMethod();
            string methodName = method.Name;
            string className = method.ReflectedType.Name;
            string fullMethodName = className + "." + methodName;

            _logger.LogInformation($"C# Timer trigger function {fullMethodName} executed at: {DateTime.Now}");
            _logger.LogInformation($"Next timer schedule at: {timerInfo.ScheduleStatus.Next}");


            ConnectToCosmosDb();
            ConnectToGraph();

            if (_container == null)
            {
                _logger.LogError($"{fullMethodName} Error: Failed to connect to Cosmos DB, exiting");
                return;
            }
            if (_graphClient == null)
            {
                _logger.LogError($"{fullMethodName} Error: Failed to connect to Graph, exiting");
                return;
            }


            FunctionSettings settings = await GetFunctionSettings();
            DateTime lastRun = settings.LastRun == null ? DateTime.UtcNow.AddDays(-30) : ((DateTime)settings.LastRun).AddHours(-1);
            List<Microsoft.Graph.Models.ManagedDevice> devices = await GetNewDeviceManagementObjectsAsync(lastRun);
            if (devices == null)
            {
                _logger.LogError($"{fullMethodName} Error: Failed to get new devices, exiting");
                return;
            }
            foreach (Microsoft.Graph.Models.ManagedDevice device in devices)
            {
                await RunDeviceUpdateActionsAsync(device);
            }
            await UpdateFunctionSettings();
        }

        private async Task RunDeviceUpdateActionsAsync(Microsoft.Graph.Models.ManagedDevice device)
        {
            List<DeviceUpdateAction> actions = new List<DeviceUpdateAction>();
            var databaseName = "DelegationStation";
            var containerName = "Device";
            var connectionString = Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING", EnvironmentVariableTarget.Process);
            var defaultActionDisable = Environment.GetEnvironmentVariable("DefaultActionDisable", EnvironmentVariableTarget.Process);

            MethodBase method = System.Reflection.MethodBase.GetCurrentMethod();
            string methodName = method.Name;
            string className = method.ReflectedType.Name;
            string fullMethodName = className + "." + methodName;

            if (String.IsNullOrEmpty(connectionString))
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"{fullMethodName} Error: Missing required environment variables. Please check the following environment variables are set:");
                sb.Append(String.IsNullOrEmpty(connectionString) ? "COSMOS_CONNECTION_STRING\n" : "");
                _logger.LogError(sb.ToString());
                return;
            }
            if (String.IsNullOrEmpty(defaultActionDisable))
            {
                _logger.LogInformation($"{fullMethodName} DefaultActionDisable environment variable not set. Defaulting to false");
            }

            CosmosClient client = new(
                connectionString: connectionString
            );
            Microsoft.Azure.Cosmos.Container container = client.GetContainer(databaseName, containerName);

            _logger.LogInformation("Connected to Cosmos DB");

            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.Type = \"Device\" AND c.Make = @manufacturer AND c.Model = @model AND c.SerialNumber = @serialNumber")
                .WithParameter("@manufacturer", device.Manufacturer.Trim())
                .WithParameter("@model", device.Model.Trim())
                .WithParameter("@serialNumber", device.SerialNumber.Trim());
            var queryIterator = container.GetItemQueryIterator<DelegationStationShared.Models.Device>(query);

            List<DelegationStationShared.Models.Device> deviceResults = new List<DelegationStationShared.Models.Device>();
            try
            {
                while (queryIterator.HasMoreResults)
                {
                    var response = await queryIterator.ReadNextAsync();
                    deviceResults.AddRange(response.ToList());
                }
                _logger.LogInformation($"{fullMethodName} Found {deviceResults.Count}", deviceResults);
            }
            catch (Exception ex)
            {
                _logger.LogError($"{fullMethodName} Error: querying Cosmos DB for device {device.Manufacturer} {device.Model} {device.SerialNumber}.\n {ex.Message}", ex);
            }

            if (deviceResults.Count < 1)
            {
                // TODO make personal / add to group / update attribute
                if (defaultActionDisable == "true")
                {
                    _logger.LogInformation($"{fullMethodName} Information: DefaultActionDisable is true. Disabling device {device.AzureADDeviceId} {device.Manufacturer} {device.Model} {device.SerialNumber}");
                    await UpdateAttributesOnDeviceAsync(device.AzureADDeviceId, new List<DeviceUpdateAction> { new DeviceUpdateAction() { ActionType = DeviceUpdateActionType.Attribute, Name = "AccountEnabled", Value = "false" } });
                }
                _logger.LogWarning($"{fullMethodName} Warning: Did not find any devices matching '{device.Manufacturer}' '{device.Model}' '{device.SerialNumber}'\nDefaultActionDisable is false.");
                return;
            }

            DelegationStationShared.Models.Device d = deviceResults.FirstOrDefault();
            foreach (string tagId in d.Tags)
            {
                DeviceTag tag = new DeviceTag();
                try
                {
                    ItemResponse<DeviceTag> tagResponse = await container.ReadItemAsync<DeviceTag>(tagId, new PartitionKey("DeviceTag"));
                    tag = tagResponse.Resource;
                    _logger.LogInformation($"{fullMethodName} Information: Found tag {tag.Name}", tag);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"{fullMethodName} Error: Get tag {tagId} failed.\n {ex.Message}", ex);
                }

                foreach (DeviceUpdateAction deviceUpdateAction in tag.UpdateActions.Where(t => t.ActionType == DeviceUpdateActionType.Group))
                {
                    await AddDeviceToAzureADGroup(device.AzureADDeviceId, deviceUpdateAction.Value);
                }

                await UpdateAttributesOnDeviceAsync(device.AzureADDeviceId, tag.UpdateActions.Where(t => t.ActionType == DeviceUpdateActionType.Attribute).ToList());

                foreach (DeviceUpdateAction deviceUpdateAction in tag.UpdateActions.Where(t => t.ActionType == DeviceUpdateActionType.AdministrativeUnit))
                {
                    await AddDeviceToAzureAdministrativeUnit(device.AzureADDeviceId, deviceUpdateAction.Value);
                }
            }
        }


        private async Task UpdateAttributesOnDeviceAsync(string deviceId, List<DeviceUpdateAction> updateActions)
        {
            MethodBase method = System.Reflection.MethodBase.GetCurrentMethod();
            string methodName = method.Name;
            string className = method.ReflectedType.Name;
            string fullMethodName = className + "." + methodName;

            if (string.IsNullOrEmpty(deviceId) || updateActions == null)
            {
                _logger.LogError($"{fullMethodName} Error: DeviceId or updateActions is null or empty. DeviceId: {deviceId};");
                return;
            }
            if (Regex.IsMatch(deviceId, _guidRegex) == false)
            {
                _logger.LogError($"{fullMethodName} Error: DeviceId is not a valid GUID. DeviceId: {deviceId}");
                return;
            }
            if (updateActions.Count < 1)
            {
                _logger.LogWarning($"{fullMethodName} Warning: No update actions configured for {deviceId}");
                return;
            }

            var requestBody = new Microsoft.Graph.Models.Device
            {
                AdditionalData = new Dictionary<string, object>
                    {
                        {
                            "extensionAttributes" , new
                            {
                                ExtensionAttribute1 = updateActions.Where(a => a.Name == "ExtensionAttribute1").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute2 = updateActions.Where(a => a.Name == "ExtensionAttribute2").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute3 = updateActions.Where(a => a.Name == "ExtensionAttribute3").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute4 = updateActions.Where(a => a.Name == "ExtensionAttribute4").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute5 = updateActions.Where(a => a.Name == "ExtensionAttribute5").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute6 = updateActions.Where(a => a.Name == "ExtensionAttribute6").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute7 = updateActions.Where(a => a.Name == "ExtensionAttribute7").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute8 = updateActions.Where(a => a.Name == "ExtensionAttribute8").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute9 = updateActions.Where(a => a.Name == "ExtensionAttribute9").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute10 = updateActions.Where(a => a.Name == "ExtensionAttribute10").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute11 = updateActions.Where(a => a.Name == "ExtensionAttribute11").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute12 = updateActions.Where(a => a.Name == "ExtensionAttribute12").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute13 = updateActions.Where(a => a.Name == "ExtensionAttribute13").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute14 = updateActions.Where(a => a.Name == "ExtensionAttribute14").FirstOrDefault()?.Value ?? "",
                                ExtensionAttribute15 = updateActions.Where(a => a.Name == "ExtensionAttribute15").FirstOrDefault()?.Value ?? "",
                            }
                        }
                    }
            };

            var result = await _graphClient.Devices[$"{deviceId}"].PatchAsync(requestBody);               
        }

        private async Task AddDeviceToAzureADGroup(string deviceId, string groupId)
        {
            MethodBase method = System.Reflection.MethodBase.GetCurrentMethod();
            string methodName = method.Name;
            string className = method.ReflectedType.Name;
            string fullMethodName = className + "." + methodName;

            if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(groupId))
            {
                _logger.LogError($"{fullMethodName} Error: DeviceId or GroupId is null or empty. DeviceId: {deviceId} GroupId: {groupId}");
                return;
            }
            if (Regex.IsMatch(deviceId, _guidRegex) == false)
            {
                _logger.LogError($"{fullMethodName} Error: DeviceId is not a valid GUID. DeviceId: {deviceId}");
                return;
            }
            if (Regex.IsMatch(groupId, _guidRegex) == false)
            {
                _logger.LogError($"{fullMethodName} Error: GroupId is not a valid GUID. GroupId: {groupId}");
                return;
            }

            try
            {
                var requestBody = new ReferenceCreate
                {
                    OdataId = $"https://graph.microsoft.com/v1.0/devices/{deviceId}"
                };
                await _graphClient.Groups[$"{groupId}"].Members.Ref.PostAsync(requestBody);
                _logger.LogInformation($"Unable to add DeviceId {deviceId} to Group {groupId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unable to add DeviceId {deviceId} to Group {groupId}");
                _logger.LogError($"Error: {ex.Message}");
            }
        }

        private async Task AddDeviceToAzureAdministrativeUnit(string deviceId, string auId)
        {
            MethodBase method = System.Reflection.MethodBase.GetCurrentMethod();
            string methodName = method.Name;
            string className = method.ReflectedType.Name;
            string fullMethodName = className + "." + methodName;

            if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(auId))
            {
                _logger.LogError($"{fullMethodName} Error: DeviceId or AU Id is null or empty. DeviceId: {deviceId} AU Id: {auId}");
                return;
            }
            if (Regex.IsMatch(deviceId, _guidRegex) == false)
            {
                _logger.LogError($"{fullMethodName} Error: DeviceId is not a valid GUID. DeviceId: {deviceId}");
                return;
            }
            if (Regex.IsMatch(auId, _guidRegex) == false)
            {
                _logger.LogError($"{fullMethodName} Error: AU Id is not a valid GUID. AU Id: {auId}");
                return;
            }

            try
            {
                var requestBody = new ReferenceCreate
                {
                    OdataId = $"https://graph.microsoft.com/v1.0/devices/{deviceId}"
                };
                await _graphClient.Directory.AdministrativeUnits[$"{auId}"].Members.Ref.PostAsync(requestBody);
                _logger.LogInformation($"Added DeviceId {deviceId} to Administrative Unit {auId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unable to add DeviceId {deviceId} to Administrative Unit {auId}");
                _logger.LogError($"Error: {ex.Message}");
            }
        }

        private async Task<List<Microsoft.Graph.Models.ManagedDevice>> GetNewDeviceManagementObjectsAsync(DateTime dateTime)
        {
                        
            List<Microsoft.Graph.Models.ManagedDevice> devices = new List<Microsoft.Graph.Models.ManagedDevice>();

            try
            {
                var managedDevices = await _graphClient.DeviceManagement.ManagedDevices
                    .GetAsync((requestConfiguration) =>
                    {
                        requestConfiguration.QueryParameters.Select = ["id","manufacturer","model","serialNumber","azureADDeviceId"];
                        requestConfiguration.QueryParameters.Filter = $"enrolledDateTime ge {dateTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")}";
                    });

                var pageIterator = Microsoft.Graph.PageIterator<ManagedDevice, ManagedDeviceCollectionResponse>
                    .CreatePageIterator(_graphClient, managedDevices, (device) =>
                    {
                        devices.Add(device);
                        return true;
                    });

                await pageIterator.IterateAsync();

                _logger.LogInformation($"Intune device information\nFound {devices.Count} new devices since {dateTime} UTC");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error: {ex.Message}");
            }

            return devices;
        }

        
        private async Task<FunctionSettings> GetFunctionSettings()
        {
            MethodBase method = System.Reflection.MethodBase.GetCurrentMethod();
            string methodName = method.Name;
            string className = method.ReflectedType.Name;
            string fullMethodName = className + "." + methodName;
            FunctionSettings settings = new FunctionSettings();
            try
            {
                settings = await _container.ReadItemAsync<FunctionSettings>(settings.Id.ToString(), new PartitionKey(settings.PartitionKey));
            }
            catch (Exception ex)
            {
                _logger.LogError($"{fullMethodName} Error: {ex.Message}");
            }
            finally
            {
                if (settings == null)
                {
                    settings = new FunctionSettings();
                    settings.LastRun = DateTime.UtcNow;
                }
                else if (settings.LastRun == null)
                {
                    settings = new FunctionSettings();
                    settings.LastRun = DateTime.UtcNow;
                }
            }


            return settings;
        }

        private async Task UpdateFunctionSettings()
        {
            MethodBase method = System.Reflection.MethodBase.GetCurrentMethod();
            string methodName = method.Name;
            string className = method.ReflectedType.Name;
            string fullMethodName = className + "." + methodName;
            FunctionSettings settings = new FunctionSettings();
            settings.LastRun = DateTime.UtcNow;
            try
            {
                var response = await _container.UpsertItemAsync<FunctionSettings>(settings, new PartitionKey(settings.PartitionKey));
                _logger.LogInformation($"{fullMethodName} Successfully updated function settings");
            }
            catch (Exception ex)
            {
                _logger.LogError($"{fullMethodName} Error: {ex.Message}");
            }
        }

        private void ConnectToCosmosDb()
        {
            var databaseName = "DelegationStation";
            var containerName = "DeviceData";
            var connectionString = Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING", EnvironmentVariableTarget.Process);
            var defaultActionDisable = Environment.GetEnvironmentVariable("DefaultActionDisable", EnvironmentVariableTarget.Process);

            MethodBase method = System.Reflection.MethodBase.GetCurrentMethod();
            string methodName = method.Name;
            string className = method.ReflectedType.Name;
            string fullMethodName = className + "." + methodName;

            if (String.IsNullOrEmpty(connectionString) || String.IsNullOrEmpty(defaultActionDisable))
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"{fullMethodName} Error: Missing required environment variables. Please check the following environment variables are set:");
                sb.Append(String.IsNullOrEmpty(connectionString) ? "COSMOS_CONNECTION_STRING\n" : "");
                sb.Append(String.IsNullOrEmpty(defaultActionDisable) ? "DefaultActionDisable\n" : "");
                _logger.LogError(sb.ToString());
                return;
            }
            try
            {
                CosmosClient client = new(
                    connectionString: connectionString
                );
                _container = client.GetContainer(databaseName, containerName);
            }
            catch (Exception ex)
            {
                _logger.LogError($"{fullMethodName} Error: Failed to connect to Cosmos DB\n{ex.Message}");
            }
        }

        private void ConnectToGraph()
        {
            var options = new TokenCredentialOptions
            {
                AuthorityHost = Environment.GetEnvironmentVariable("AzureEnvironment", EnvironmentVariableTarget.Process) == "AzurePublicCloud" ? AzureAuthorityHosts.AzurePublicCloud : AzureAuthorityHosts.AzureGovernment
            };

            var subject = Environment.GetEnvironmentVariable("CertificateDistinguishedName", EnvironmentVariableTarget.Process);

            var TenantId = Environment.GetEnvironmentVariable("AzureAd:TenantId", EnvironmentVariableTarget.Process);
            var ClientId = Environment.GetEnvironmentVariable("AzureAd:ClientId", EnvironmentVariableTarget.Process);
            var ClientSecret = Environment.GetEnvironmentVariable("AzureApp:ClientSecret", EnvironmentVariableTarget.Process);



            if (string.IsNullOrEmpty(TenantId) || string.IsNullOrEmpty(ClientId) || (string.IsNullOrEmpty(ClientSecret) && string.IsNullOrEmpty(subject)))
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Missing required environment variables. Please check the following environment variables are set:");
                sb.Append(string.IsNullOrEmpty(TenantId) ? "AzureAd:TenantId\n" : "");
                sb.Append(string.IsNullOrEmpty(ClientId) ? "AzureAd:ClientId\n" : "");
                sb.Append(string.IsNullOrEmpty(ClientSecret) && string.IsNullOrEmpty(subject) ? "AzureApp:ClientSecret or CertificateDistinguishedName\n" : "");
                _logger.LogError(sb.ToString());
                return;
            }

            if (string.IsNullOrEmpty(ClientSecret) && !string.IsNullOrEmpty(subject))
            {
                _logger.LogInformation("Using certificate authentication");
            }
            else
            {
                _logger.LogInformation("Using client secret authentication");
            }

            if (subject != null)
            {
                X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);

                var certificate = store.Certificates.Cast<X509Certificate2>().FirstOrDefault(cert => cert.Subject == subject);

                var clientCertCredential = new ClientCertificateCredential(
                    TenantId,
                    ClientId,
                    certificate,
                    options
                );
                store.Close();
                _graphClient = new GraphServiceClient(clientCertCredential);
            }
            else
            {
                var clientSecretCredential = new ClientSecretCredential(
                    TenantId,
                    ClientId,
                    ClientSecret,
                    options
                );

                _graphClient = new GraphServiceClient(clientSecretCredential);
            }
        }

        public class TimerInfo
        {
            public TimerScheduleStatus ScheduleStatus { get; set; }

            public bool IsPastDue { get; set; }
        }

        public class TimerScheduleStatus
        {
            public DateTime Last { get; set; }

            public DateTime Next { get; set; }

            public DateTime LastUpdated { get; set; }
        }
    }

}
