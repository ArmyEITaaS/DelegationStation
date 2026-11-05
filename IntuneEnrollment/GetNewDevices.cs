using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using DelegationStationShared.Models;
using IntuneEnrollment.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace IntuneEnrollment
{
    public class GetNewDevices
    {
        private static ILogger _logger;
        private static string _guidRegex = "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$";
        private static Microsoft.Azure.Cosmos.Container _container = null;

        [FunctionName("GetNewDevices")]
        public async Task Run([TimerTrigger("%TriggerTime%")]TimerInfo myTimer, ILogger log)
        {
            MethodBase method = System.Reflection.MethodBase.GetCurrentMethod();
            string methodName = method.Name;
            string className = method.ReflectedType.Name;
            string fullMethodName = className + "." + methodName;

            _logger = log;

            _logger.LogInformation($"C# Timer trigger function {fullMethodName} executed at: {DateTime.Now}");

            ConnectToCosmosDb();
            if(_container == null)
            {
                _logger.LogError($"{fullMethodName} Error: Failed to connect to Cosmos DB, exiting");
                return;
            }


            FunctionSettings settings = await GetFunctionSettings();
            DateTime lastRun = settings.LastRun == null ? DateTime.UtcNow.AddDays(-30) : ((DateTime)settings.LastRun).AddHours(-1);
            List<DelegationStationShared.Models.Graph.ManagedDevice> devices = await GetNewDeviceManagementObjectsAsync(lastRun);
            if (devices == null)
            {
                _logger.LogError($"{fullMethodName} Error: Failed to get new devices, exiting");
                return;
            }
            foreach (DelegationStationShared.Models.Graph.ManagedDevice device in devices)
            {
                await RunDeviceUpdateActionsAsync(device);
            }
            await UpdateFunctionSettings();
        }

        private static async Task<List<DelegationStationShared.Models.Graph.ManagedDevice>> GetNewDeviceManagementObjectsAsync(DateTime dateTime)
        {
            MethodBase method = System.Reflection.MethodBase.GetCurrentMethod();
            string methodName = method.Name;
            string className = method.ReflectedType.Name;
            string fullMethodName = className + "." + methodName;

            var TargetCloud = Environment.GetEnvironmentVariable("AzureEnvironment", EnvironmentVariableTarget.Process);
            
            string tokenUri = "";
            
            if (TargetCloud == "AzureUSDoD")
            {
                tokenUri = "https://dod-graph.microsoft.us";
            }
            else if(TargetCloud == "AzureUSGovernment")
            {
                tokenUri = "https://graph.microsoft.us";
            }
            else
            {
                tokenUri = "https://graph.microsoft.com";
            }

            var graphAccessToken = await GetAccessTokenAsync(tokenUri);
            if (graphAccessToken == null)
            {
                _logger.LogError($"{fullMethodName} Error: Failed to get access token");
                return null;
            }

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);
            List<DelegationStationShared.Models.Graph.ManagedDevice> devices = new List<DelegationStationShared.Models.Graph.ManagedDevice>();

            try
            {
                string uri = $"{tokenUri}/v1.0/deviceManagement/managedDevices?$filter=enrolledDateTime ge {dateTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")}&$select=id,manufacturer,model,serialNumber,azureADDeviceId";
                while(!string.IsNullOrEmpty(uri))
                {
                    var graphResponse = await httpClient.GetAsync(uri);
                    graphResponse.EnsureSuccessStatusCode();
                    var graphContent = await graphResponse.Content.ReadAsStringAsync();
                    DelegationStationShared.Models.Graph.ManagedDevices deviceResponse = JsonConvert.DeserializeObject<DelegationStationShared.Models.Graph.ManagedDevices>(graphContent);
                    
                    devices.AddRange(deviceResponse.value);
                    uri = deviceResponse.odataNextLink;
                }

                _logger.LogInformation($"{fullMethodName} Intune device information\nFound {devices.Count} new devices since {dateTime} UTC");
            }
            catch (Exception ex)
            {
                _logger.LogError($"{fullMethodName} Error: {ex.Message}");
            }
            
            return devices;
        }

        private static async Task<FunctionSettings> GetFunctionSettings()
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
                else  if (settings.LastRun == null)
                {
                    settings = new FunctionSettings();
                    settings.LastRun = DateTime.UtcNow;
                }
            }
            

            return settings;
        }

        private static async Task UpdateFunctionSettings()
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
        private static void ConnectToCosmosDb()
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
        private static async Task RunDeviceUpdateActionsAsync(DelegationStationShared.Models.Graph.ManagedDevice device)
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
            if(String.IsNullOrEmpty(defaultActionDisable))
            {
                _logger.LogInformation($"{fullMethodName} DefaultActionDisable environment variable not set. Defaulting to false");
            }

            CosmosClient client = new(
                connectionString: connectionString
            );
            Microsoft.Azure.Cosmos.Container container = client.GetContainer(databaseName, containerName);

            _logger.LogInformation("Connected to Cosmos DB");

            QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.Type = \"Device\" AND c.Make = @manufacturer AND c.Model = @model AND c.SerialNumber = @serialNumber")
                .WithParameter("@manufacturer", device.manufacturer.Trim())
                .WithParameter("@model", device.model.Trim())
                .WithParameter("@serialNumber", device.serialNumber.Trim());
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
                _logger.LogError($"{fullMethodName} Error: querying Cosmos DB for device {device.manufacturer} {device.model} {device.serialNumber}.\n {ex.Message}", ex);
            }

            if (deviceResults.Count < 1)
            {
                // TODO make personal / add to group / update attribute
                if (defaultActionDisable == "true")
                {
                    _logger.LogInformation($"{fullMethodName} Information: DefaultActionDisable is true. Disabling device {device.azureADDeviceId} {device.manufacturer} {device.model} {device.serialNumber}");
                    await UpdateAttributesOnDeviceAsync(device.azureADDeviceId, new List<DeviceUpdateAction> { new DeviceUpdateAction() { ActionType = DeviceUpdateActionType.Attribute, Name = "AccountEnabled", Value = "false" } });
                }
                _logger.LogWarning($"{fullMethodName} Warning: Did not find any devices matching '{device.manufacturer}' '{device.model}' '{device.serialNumber}'");
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
                    await AddDeviceToAzureADGroup(device.azureADDeviceId, deviceUpdateAction.Value);
                }

                await UpdateAttributesOnDeviceAsync(device.azureADDeviceId, tag.UpdateActions.Where(t => t.ActionType == DeviceUpdateActionType.Attribute).ToList());

                foreach(DeviceUpdateAction deviceUpdateAction in tag.UpdateActions.Where(t => t.ActionType == DeviceUpdateActionType.AdministrativeUnit))
                {
                    await AddDeviceToAzureAdministrativeUnit(device.azureADDeviceId, deviceUpdateAction.Value);
                }
            }
        }

        private static async Task AddDeviceToAzureADGroup(string deviceId, string groupId)
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

            var TargetCloud = Environment.GetEnvironmentVariable("AzureEnvironment", EnvironmentVariableTarget.Process);

            string tokenUri = "";
            if (TargetCloud == "AzureUSDoD")
            {
                tokenUri = "https://dod-graph.microsoft.us";
            }
            else if (TargetCloud == "AzureUSGovernment")
            {
                tokenUri = "https://graph.microsoft.us";
            }
            else
            {
                tokenUri = "https://graph.microsoft.com";
            }


            try
            {

                var graphAccessToken = await GetAccessTokenAsync(tokenUri);
                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);

                string deviceADRequestUri = $"{tokenUri}/v1.0/devices?$filter=deviceId eq '{deviceId}'&$select=id";
                var deviceADRequest = new HttpRequestMessage(HttpMethod.Get, deviceADRequestUri);
                deviceADRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);
                HttpResponseMessage deviceADResponse = await httpClient.SendAsync(deviceADRequest);
                deviceADResponse.EnsureSuccessStatusCode();
                string deviceADResponseContent = await deviceADResponse.Content.ReadAsStringAsync();
                Regex regex = new Regex(@"\b[A-Fa-f0-9]{8}(?:-[A-Fa-f0-9]{4}){3}-[A-Fa-f0-9]{12}\b");
                string deviceAzureAdObjectId = "";
                Match deviceMatch = regex.Match(deviceADResponseContent);
                if (deviceMatch.Success)
                {
                    deviceAzureAdObjectId = deviceMatch.Value;
                }
                else
                {
                    _logger.LogError($"{fullMethodName} Error: Could not find Azure AD Object Id for device {deviceId}");
                    return;
                }

                var groupRequest = new HttpRequestMessage(HttpMethod.Post, $"{tokenUri}/v1.0/groups/{groupId}/members/$ref");
                groupRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);

                var values = new Dictionary<string, string>{
                    { "@odata.id", $"{tokenUri}/v1.0/directoryObjects/{deviceAzureAdObjectId}" }
                };

                var json = JsonConvert.SerializeObject(values, Formatting.Indented);

                var stringContent = new StringContent(json, System.Text.Encoding.UTF8, "text/plain");

                _logger.LogInformation($"{fullMethodName} Adding Device:{deviceAzureAdObjectId} to Group:{groupId}");

                stringContent.Headers.Remove("Content-Type");
                stringContent.Headers.Add("Content-Type", "application/json");
                groupRequest.Content = stringContent;

                var groupResponse = await httpClient.SendAsync(groupRequest);
                if (groupResponse.StatusCode == System.Net.HttpStatusCode.OK || groupResponse.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    _logger.LogInformation($"{fullMethodName} Device:{deviceAzureAdObjectId} added to Group:{groupId}");
                }
                else if (groupResponse.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    string groupResponseContent = await groupResponse.Content.ReadAsStringAsync();
                    if (groupResponseContent.Contains("One or more added object references already exist for the following modified properties: 'members'."))
                    {
                        _logger.LogInformation($"{fullMethodName} Device:{deviceAzureAdObjectId} already added to Group:{groupId}");
                    }
                    else
                    {
                        _logger.LogError($"{fullMethodName} Error: Device:{deviceAzureAdObjectId} not added to Group:{groupId}\n{await groupResponse.Content.ReadAsStringAsync()}");
                    }
                }
                else
                {
                    _logger.LogError($"{fullMethodName} Error: Device:{deviceId} not added to Group:{groupId}\n{await groupResponse.Content.ReadAsStringAsync()}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{fullMethodName} Error: {ex.Message}");
            }
        }

        private static async Task AddDeviceToAzureAdministrativeUnit(string deviceId, string auId)
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

            var TargetCloud = Environment.GetEnvironmentVariable("AzureEnvironment", EnvironmentVariableTarget.Process);

            string tokenUri = "";
            if (TargetCloud == "AzureUSDoD")
            {
                tokenUri = "https://dod-graph.microsoft.us";
            }
            else if (TargetCloud == "AzureUSGovernment")
            {
                tokenUri = "https://graph.microsoft.us";
            }
            else
            {
                tokenUri = "https://graph.microsoft.com";
            }

            var graphAccessToken = await GetAccessTokenAsync(tokenUri);
            if (graphAccessToken == null)
            {
                _logger.LogError($"{fullMethodName} Error: Failed to get Graph Access Token");
                return;
            }

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);

            string deviceADRequestUri = $"{tokenUri}/v1.0/devices?$filter=deviceId eq '{deviceId}'&$select=id";
            var deviceADRequest = new HttpRequestMessage(HttpMethod.Get, deviceADRequestUri);
            deviceADRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);
            HttpResponseMessage deviceADResponse = await httpClient.SendAsync(deviceADRequest);
            deviceADResponse.EnsureSuccessStatusCode();
            string deviceADResponseContent = await deviceADResponse.Content.ReadAsStringAsync();
            Regex regex = new Regex(@"\b[A-Fa-f0-9]{8}(?:-[A-Fa-f0-9]{4}){3}-[A-Fa-f0-9]{12}\b");
            string deviceAzureAdObjectId = "";
            Match deviceMatch = regex.Match(deviceADResponseContent);
            if (deviceMatch.Success)
            {
                deviceAzureAdObjectId = deviceMatch.Value;
            }
            else
            {
                _logger.LogError($"{fullMethodName} Error: Could not find Azure AD Object Id for device {deviceId}");
                return;
            }

            var groupRequest = new HttpRequestMessage(HttpMethod.Post, $"{tokenUri}/v1.0/directory/administrativeUnits/{auId}/members/$ref");
            groupRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);

            var values = new Dictionary<string, string>{
                { "@odata.id", $"{tokenUri}/v1.0/devices/{deviceAzureAdObjectId}" }
            };

            var json = JsonConvert.SerializeObject(values, Formatting.Indented);

            var stringContent = new StringContent(json, System.Text.Encoding.UTF8, "text/plain");

            _logger.LogInformation($"Adding Device:{deviceAzureAdObjectId} to Administrative Unit:{auId}");

            stringContent.Headers.Remove("Content-Type");
            stringContent.Headers.Add("Content-Type", "application/json");
            groupRequest.Content = stringContent;

            var groupResponse = await httpClient.SendAsync(groupRequest);
            string response = await groupResponse.Content.ReadAsStringAsync();
            if (groupResponse.StatusCode == System.Net.HttpStatusCode.OK || groupResponse.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                _logger.LogInformation($"Device:{deviceId} added to Administrative Unit:{auId}");
            }
            else if (groupResponse.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                if (response.Contains("A conflicting object with one or more of the specified property values is present in the directory"))
                {
                    _logger.LogInformation($"Device:{deviceId} already exists in Administrative Unit:{auId}");
                }
                else
                {
                    _logger.LogError($"Error: Device:{deviceId} not added to Administrative Unit:{auId}\n{response}");
                }
            }
            else
            {
                _logger.LogError($"Error: Device:{deviceId} not added to Administrative Unit:{auId}\n{response}");
            }
            response = String.IsNullOrEmpty(response) ? "Success" : response;
            _logger.LogInformation($"Administrative Unit Add Response: {response}");
        }


        private static async Task UpdateAttributesOnDeviceAsync(string deviceId, List<DeviceUpdateAction> updateActions)
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

            var TargetCloud = Environment.GetEnvironmentVariable("AzureEnvironment", EnvironmentVariableTarget.Process);
            _logger.LogInformation($"{fullMethodName} Information: Updating attributes on {deviceId}");
            string tokenUri = "";
            if (TargetCloud == "AzureUSDoD")
            {
                tokenUri = "https://dod-graph.microsoft.us";
            }
            else if (TargetCloud == "AzureUSGovernment")
            {
                tokenUri = "https://graph.microsoft.us";
            }
            else
            {
                tokenUri = "https://graph.microsoft.com";
            }

            var graphAccessToken = await GetAccessTokenAsync(tokenUri);
            if (string.IsNullOrEmpty(graphAccessToken))
            {
                _logger.LogError($"{fullMethodName} Error: Failed to get Graph access token for Device Id {deviceId}");
                return;
            }

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);



            foreach (var action in updateActions)
            {
                string attributeContent = "";
                _logger.LogDebug($"{fullMethodName} Debug:\nDevice: {deviceId}\nAction: {action.Name}\nValue: {action.Value}", action);

                switch (action.Name)
                {
                    case "ExtensionAttribute1":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute1 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute2":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute2 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute3":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute3 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute4":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute4 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute5":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute5 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute6":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute6 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute7":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute7 = action.Value,
                                }
                            },
                        });
                        break;
                    //case for extension attributes 8-15
                    case "ExtensionAttribute8":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute8 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute9":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute9 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute10":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                            {
                                {
                                "extensionAttributes", new
                                {
                                    extensionAttribute10 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute11":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute11 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute12":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute12 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute13":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute13 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute14":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute14 = action.Value,
                                }
                            },
                        });
                        break;
                    case "ExtensionAttribute15":
                        attributeContent = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            {
                                "extensionAttributes", new
                                {
                                    extensionAttribute15 = action.Value,
                                }
                            },
                        });
                        break;
                    case "AccountEnabled":
                        attributeContent = JsonConvert.SerializeObject(new
                        {
                            accountEnabled = action.Value
                        });
                        break;
                    default:
                        _logger.LogWarning($"{fullMethodName} Warning: Property update called on a property not implemented. Property - {action.Name}");

                        break;
                }
                string deviceADRequestUri = String.Format("{0}/v1.0/devices(deviceId='{1}')", tokenUri, deviceId);

                var deviceADRequest = new HttpRequestMessage(HttpMethod.Patch, deviceADRequestUri)
                {
                    Content = new StringContent(attributeContent, System.Text.Encoding.UTF8, "application/json")
                };
                deviceADRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);
                try
                {
                    HttpResponseMessage deviceADResponse = await httpClient.SendAsync(deviceADRequest);
                    if (deviceADResponse.IsSuccessStatusCode)
                    {
                        _logger.LogInformation($"{fullMethodName} Graph Response Success: {deviceADResponse.IsSuccessStatusCode}\nStatus Code: {deviceADResponse.StatusCode}\nReason: {deviceADResponse.ReasonPhrase}\nRequest URI: {deviceADRequestUri}\nAttributeContent: {attributeContent}");
                    }
                    else
                    {
                        _logger.LogError($"{fullMethodName} Graph Response Error: {deviceADResponse.IsSuccessStatusCode}\nStatus Code: {deviceADResponse.StatusCode}\nReason: {deviceADResponse.ReasonPhrase}\nRequest URI: {deviceADRequestUri}\nAttributeContent: {attributeContent}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"{fullMethodName} Graph Response Error: {ex.Message}\nRequest URI: {deviceADRequestUri}\nAttributeContent: {attributeContent}");
                    return;
                }
            }

            return;
        }



        private static async Task<String> GetAccessTokenAsync(string uri)
        {
            MethodBase method = System.Reflection.MethodBase.GetCurrentMethod();
            string methodName = method.Name;
            string className = method.ReflectedType.Name;
            string fullMethodName = className + "." + methodName;

            var AppSecret = Environment.GetEnvironmentVariable("AzureApp:ClientSecret", EnvironmentVariableTarget.Process);
            var AppId = Environment.GetEnvironmentVariable("AzureAd:ClientId", EnvironmentVariableTarget.Process);
            var TenantId = Environment.GetEnvironmentVariable("AzureAd:TenantId", EnvironmentVariableTarget.Process);
            var TargetCloud = Environment.GetEnvironmentVariable("AzureEnvironment", EnvironmentVariableTarget.Process);

            if (String.IsNullOrEmpty(AppSecret) || String.IsNullOrEmpty(AppId) || String.IsNullOrEmpty(TenantId) || String.IsNullOrEmpty(TargetCloud))
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"{fullMethodName} Error: Missing required environment variables. Please check the following environment variables are set:");
                sb.Append(String.IsNullOrEmpty(AppSecret) ? "AzureApp:ClientSecret\n" : "");
                sb.Append(String.IsNullOrEmpty(AppId) ? "AzureAd:ClientId\n" : "");
                sb.Append(String.IsNullOrEmpty(TenantId) ? "AzureAd:TenantId\n" : "");
                sb.Append(String.IsNullOrEmpty(TargetCloud) ? "AzureEnvironment\n" : "");
                _logger.LogError(sb.ToString());
                return null;
            }

            string tokenUri = "";
          
            if(TargetCloud == "AzureUSGovernment" || TargetCloud == "AzureUSDoD")
            {
                tokenUri = $"https://login.microsoftonline.us/{TenantId}/oauth2/token";
            }
            else
            {
                tokenUri = $"https://login.microsoftonline.com/{TenantId}/oauth2/token";
            }

            // Get token for Log Analytics

            var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenUri);
            tokenRequest.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", AppId),
                new KeyValuePair<string, string>("client_secret", AppSecret),
                new KeyValuePair<string, string>("resource", uri)
            });

            try
            {
                var httpClient = new HttpClient();
                var tokenResponse = await httpClient.SendAsync(tokenRequest);
                var tokenContent = await tokenResponse.Content.ReadAsStringAsync();
                var tokenData = JsonConvert.DeserializeObject<dynamic>(tokenContent);
                return tokenData.access_token;
            }
            catch (Exception ex)
            {
                _logger.LogError($"{fullMethodName} Error: getting access token for URI {tokenUri}: {ex.Message}");
                return null;
            }
        }
    }
}
