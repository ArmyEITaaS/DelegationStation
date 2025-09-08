using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Azure.Cosmos;
using System.Linq;
using System.Reflection;
using DelegationStationShared.Models;
using System.Text;
using DelegationStationShared;


namespace DelegationStation.Function
{
    public static class IntuneEnrollmentFunction
    {
        //private static GraphServiceClient _graphClient;
        private static ILogger _logger;
        //private static HttpClient _graphHttpClient;
        private static string _guidRegex = "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$";

        [FunctionName("IntuneEnrollmentTrigger")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "IntuneEnrollmentTrigger")] HttpRequest req,
            ILogger log)
        {
            _logger = log;

            //TODO:  validate request against schema
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            // FIXME:  commenting out - need to validate and sanitize the input
            //_logger.LogDebug($"RequestBody: \n{requestBody}");

            string response = "Completed execution";

            string logUri = GetLogAnalyticsUri(requestBody);
            List<string> deviceIds = await GetDeviceIdsFromLogAnalyticsAsync(logUri);
            if(deviceIds == null || deviceIds.Count < 1)
            {
                response = "No devices found in log analytics";
                _logger.LogWarning(response);
                return new BadRequestObjectResult(response);
            }

            List<DeviceResponse> devices = await GetDeviceManagementObjectsAsync(deviceIds.ToArray());
            if (devices == null || devices.Count < 1)
            {
                response = "No devices found in Intune";
                _logger.LogWarning(response);
                return new BadRequestObjectResult(response);
            }

            foreach (DeviceResponse device in devices)
            {
                await RunDeviceUpdateActionsAsync(device);
            }

            return new OkObjectResult(response);
        }


        private static async Task RunDeviceUpdateActionsAsync(DeviceResponse device)
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

            if (String.IsNullOrEmpty(connectionString) || String.IsNullOrEmpty(defaultActionDisable))
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"{fullMethodName} Error: Missing required environment variables. Please check the following environment variables are set:");
                sb.Append(String.IsNullOrEmpty(connectionString) ? "COSMOS_CONNECTION_STRING\n" : "");
                sb.Append(String.IsNullOrEmpty(defaultActionDisable) ? "DefaultActionDisable\n" : "");
                _logger.LogError(sb.ToString());
                return;
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

            if(deviceResults.Count < 1)
            {
                // TODO make personal / add to group / update attribute
                if(defaultActionDisable == "true")
                {
                    _logger.LogInformation($"{fullMethodName} Information: DefaultActionDisable is true. Disabling device {device.AzureADDeviceId} {device.Manufacturer} {device.Model} {device.SerialNumber}");
                    await UpdateAttributesOnDeviceAsync(device.AzureADDeviceId, new List<DeviceUpdateAction> { new DeviceUpdateAction() { ActionType = DeviceUpdateActionType.Attribute, Name = "AccountEnabled", Value = "false" } });
                }
                _logger.LogWarning($"{fullMethodName} Warning: Did not find any devices matching '{device.Manufacturer}' '{device.Model}' '{device.SerialNumber}'");
                return;
            }

            DelegationStationShared.Models.Device d = deviceResults.FirstOrDefault();
            foreach(string tagId in d.Tags)
            {
                DeviceTag tag = new DeviceTag();
                try
                {
                    ItemResponse<DeviceTag> tagResponse = await container.ReadItemAsync<DeviceTag>(tagId, new PartitionKey("DeviceTag"));
                    tag = tagResponse.Resource;
                    _logger.LogInformation($"{fullMethodName} Information: Found tag {tag.Name}", tag);
                }
                catch(Exception ex)
                {
                    _logger.LogError($"{fullMethodName} Error: Get tag {tagId} failed.\n {ex.Message}", ex);
                }

                foreach(DeviceUpdateAction deviceUpdateAction in tag.UpdateActions.Where(t => t.ActionType == DeviceUpdateActionType.Group))
                {
                    await AddDeviceToAzureADGroup(device.AzureADDeviceId, deviceUpdateAction.Value);
                }

                await UpdateAttributesOnDeviceAsync(device.AzureADDeviceId, tag.UpdateActions.Where(t => t.ActionType == DeviceUpdateActionType.Attribute).ToList());
            }

            return;
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
            if (Regex.IsMatch(deviceId, DSConstants.GUID_REGEX) == false)
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
            if (TargetCloud == "AzurePublicCloud")
            {
                tokenUri = "https://graph.microsoft.com";
            }
            else
            {
                // TODO update URI
                tokenUri = "https://graph.microsoft.us";
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
                _logger.LogDebug($"{fullMethodName} Debug:\nDevice: {deviceId}\nAction: {action.Name}\nValue: {action.Value}",action);

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

        private static string GetLogAnalyticsUri(string requestBody)
        {
            string logAnalyticsRegexPattern = @"\""linkToSearchResultsAPI\"":\s?\""(\S+)\""";
            Match logAnalyticsMatch = Regex.Match(requestBody, logAnalyticsRegexPattern);
            string logUri = "";
            if (logAnalyticsMatch.Success)
            {
                logUri = logAnalyticsMatch.Groups[1].Value;
                //FIXME:  Need to sanitize/validate logURI before logging
                // commenting out for now
                //_logger.LogInformation($"Log Analytics Uri Used: {logUri}");
            }
            else
            {
                //FIXME:  Need to santizie/validate logURI before logging
                // commenting out for now
                _logger.LogInformation($"Error: Unable to find Log Analytics Uri:\n");
                //_logger.LogInformation($"Error: Unable to find Log Analytics Uri:\n{requestBody}");
            }
            return logUri;
        }

        private static async Task<List<String>> GetDeviceIdsFromLogAnalyticsAsync(string logUri)
        {
            MethodBase method = System.Reflection.MethodBase.GetCurrentMethod();
            string methodName = method.Name;
            string className = method.ReflectedType.Name;
            string fullMethodName = className + "." + methodName;

            var httpClient = new HttpClient();

            // TODO update for Azure Government
            var accessToken = await GetAccessTokenAsync("https://api.loganalytics.io");
            if(accessToken == null)
            {
                _logger.LogError($"{fullMethodName} Error: Unable to get access token for Log Analytics");
                return null;
            }

            List<String> deviceIds = new List<String>();

            var logAnalyticsRequest = new HttpRequestMessage(HttpMethod.Get, logUri);
            logAnalyticsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var logAnalyticsResponse = await httpClient.SendAsync(logAnalyticsRequest);
            var logAContent = await logAnalyticsResponse.Content.ReadAsStringAsync();

            Regex regex = new Regex(@"\b[A-Fa-f0-9]{8}(?:-[A-Fa-f0-9]{4}){3}-[A-Fa-f0-9]{12}\b");
            foreach (Match m in regex.Matches(logAContent))
            {
                deviceIds.Add(m.Value);
                _logger.LogInformation($"Device Id found in LogAnalyticsQuery: {m.Value}");
            }
            return deviceIds;
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
            if (Regex.IsMatch(deviceId, DSConstants.GUID_REGEX) == false)
            {
                _logger.LogError($"{fullMethodName} Error: DeviceId is not a valid GUID. DeviceId: {deviceId}");
                return;
            }
            if (Regex.IsMatch(groupId, DSConstants.GUID_REGEX) == false)
            {
                _logger.LogError($"{fullMethodName} Error: GroupId is not a valid GUID. GroupId: {groupId}");
                return;
            }

            var TargetCloud = Environment.GetEnvironmentVariable("AzureEnvironment", EnvironmentVariableTarget.Process);

            string tokenUri = "";
            if (TargetCloud == "AzurePublicCloud")
            {
                tokenUri = "https://graph.microsoft.com";
            }
            else
            {
                // TODO update URI
                tokenUri = "https://graph.microsoft.us";
            }


            try
            {

                var graphAccessToken = await GetAccessTokenAsync(tokenUri);
                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);


                string deviceADRequestUri = $"{tokenUri}/v1.0/devices?$filter=deviceId eq '{deviceId}'";
                var deviceADRequest = new HttpRequestMessage(HttpMethod.Get, deviceADRequestUri);
                deviceADRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);
                HttpResponseMessage deviceADResponse = await httpClient.SendAsync(deviceADRequest);
                deviceADResponse.EnsureSuccessStatusCode();
                string deviceADResponseContent = await deviceADResponse.Content.ReadAsStringAsync();
                Regex regex = new Regex(@"\b[A-Fa-f0-9]{8}(?:-[A-Fa-f0-9]{4}){3}-[A-Fa-f0-9]{12}\b");
                string deviceAzureAdId = "";
                Match deviceMatch = regex.Match(deviceADResponseContent);
                if (deviceMatch.Success)
                {
                    deviceAzureAdId = deviceMatch.Value;
                }

                var groupRequest = new HttpRequestMessage(HttpMethod.Post, $"{tokenUri}/v1.0/groups/{groupId}/members/$ref");
                groupRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);

                var values = new Dictionary<string, string>{
                    { "@odata.id", $"{tokenUri}/v1.0/directoryObjects/{deviceAzureAdId}" }
                };

                var json = JsonConvert.SerializeObject(values, Formatting.Indented);

                var stringContent = new StringContent(json, System.Text.Encoding.UTF8, "text/plain");

                _logger.LogInformation($"{fullMethodName} Adding Device:{deviceId} to Group:{groupId}");

                stringContent.Headers.Remove("Content-Type");
                stringContent.Headers.Add("Content-Type", "application/json");
                groupRequest.Content = stringContent;

                var groupResponse = await httpClient.SendAsync(groupRequest);
                string response = await groupResponse.Content.ReadAsStringAsync();
                response = String.IsNullOrEmpty(response) ? "Success" : response;
                _logger.LogInformation($"{fullMethodName} Group Add Response: {response}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"{fullMethodName} Error: {ex.Message}");
            }
        }

        private static async Task AddDeviceToAzureAdministrativeUnit (string deviceId, string auId)
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
            if (Regex.IsMatch(deviceId, DSConstants.GUID_REGEX) == false)
            {
                _logger.LogError($"{fullMethodName} Error: DeviceId is not a valid GUID. DeviceId: {deviceId}");
                return;
            }
            if (Regex.IsMatch(auId, DSConstants.GUID_REGEX) == false)
            {
                _logger.LogError($"{fullMethodName} Error: AU Id is not a valid GUID. AU Id: {auId}");
                return;
            }

            var TargetCloud = Environment.GetEnvironmentVariable("AzureEnvironment", EnvironmentVariableTarget.Process);

            string tokenUri = "";
            if (TargetCloud == "AzurePublicCloud")
            {
                tokenUri = "https://graph.microsoft.com";
            }
            else
            {
                // TODO update URI
                tokenUri = "https://graph.microsoft.us";
            }

            var graphAccessToken = await GetAccessTokenAsync(tokenUri);
            if(graphAccessToken == null)
            {
                _logger.LogError($"{fullMethodName} Error: Failed to get Graph Access Token");
                return;
            }

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);


            string deviceADRequestUri = $"{tokenUri}/v1.0/devices?$filter=deviceId eq '{deviceId}'";
            var deviceADRequest = new HttpRequestMessage(HttpMethod.Get, deviceADRequestUri);
            deviceADRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);
            HttpResponseMessage deviceADResponse = await httpClient.SendAsync(deviceADRequest);
            deviceADResponse.EnsureSuccessStatusCode();
            string deviceADResponseContent = await deviceADResponse.Content.ReadAsStringAsync();
            Regex regex = new Regex(@"\b[A-Fa-f0-9]{8}(?:-[A-Fa-f0-9]{4}){3}-[A-Fa-f0-9]{12}\b");
            string deviceAzureAdId = "";
            Match deviceMatch = regex.Match(deviceADResponseContent);
            if (deviceMatch.Success)
            {
                deviceAzureAdId = deviceMatch.Value;
            }

            var groupRequest = new HttpRequestMessage(HttpMethod.Post, $"{tokenUri}/v1.0/directory/administrativeUnits/{auId}/members/$ref");
            groupRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);

            var values = new Dictionary<string, string>{
                { "@odata.id", $"{tokenUri}/v1.0/devices/{deviceAzureAdId}" }
            };

            var json = JsonConvert.SerializeObject(values, Formatting.Indented);

            var stringContent = new StringContent(json, System.Text.Encoding.UTF8, "text/plain");

            _logger.LogInformation($"Adding Device:{deviceId} to Administrative Unit:{auId}");

            stringContent.Headers.Remove("Content-Type");
            stringContent.Headers.Add("Content-Type", "application/json");
            groupRequest.Content = stringContent;

            var groupResponse = await httpClient.SendAsync(groupRequest);
            string response = await groupResponse.Content.ReadAsStringAsync();
            response = String.IsNullOrEmpty(response) ? "Success" : response;
            _logger.LogInformation($"Administrative Unit Add Response: {response}");
        }

        private static async Task<List<DeviceResponse>> GetDeviceManagementObjectsAsync(string[] deviceIds)
        {
            MethodBase method = System.Reflection.MethodBase.GetCurrentMethod();
            string methodName = method.Name;
            string className = method.ReflectedType.Name;
            string fullMethodName = className + "." + methodName;

            var TargetCloud = Environment.GetEnvironmentVariable("AzureEnvironment", EnvironmentVariableTarget.Process);

            string tokenUri = "";
            if (TargetCloud == "AzurePublicCloud")
            {
                tokenUri = "https://graph.microsoft.com";
            }
            else
            {
                // TODO update URI
                tokenUri = "https://graph.microsoft.us";
            }

            var graphAccessToken = await GetAccessTokenAsync(tokenUri);
            if(graphAccessToken == null)
            {
                _logger.LogError($"{fullMethodName} Error: Failed to get access token");
                return null;
            }

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", graphAccessToken);
            List<DeviceResponse> devices = new List<DeviceResponse>();

            foreach (string d in deviceIds)
            {
                try
                {
                    var graphResponse = await httpClient.GetAsync($"{tokenUri}/v1.0/deviceManagement/managedDevices/{d}?$select=id,manufacturer,model,serialNumber,azureADDeviceId");
                    graphResponse.EnsureSuccessStatusCode();

                    var graphContent = await graphResponse.Content.ReadAsStringAsync();
                    DeviceResponse device = JsonConvert.DeserializeObject<DeviceResponse>(graphContent);
                    devices.Add(device);
                    _logger.LogInformation($"{fullMethodName} Intune device information\nDeviceId: {device.Id}\nMake: {device.Manufacturer}\nModel: {device.Model}\nSerialNumber: {device.SerialNumber}\nAzureADId: {device.AzureADDeviceId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"{fullMethodName} Error: {ex.Message}");
                }
            }
            return devices;
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

            if(String.IsNullOrEmpty(AppSecret) || String.IsNullOrEmpty(AppId) || String.IsNullOrEmpty(TenantId) || String.IsNullOrEmpty(TargetCloud))
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
            if (TargetCloud == "AzurePublicCloud")
            {
                tokenUri = $"https://login.microsoftonline.com/{TenantId}/oauth2/token";
            }
            else
            {
                tokenUri = $"https://login.microsoftonline.us/{TenantId}/oauth2/token";
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

        private class DeviceResponse
        {
            [JsonProperty("manufacturer")]
            public string Manufacturer { get; set; }

            [JsonProperty("model")]
            public string Model { get; set; }

            [JsonProperty("serialNumber")]
            public string SerialNumber { get; set; }

            [JsonProperty("azureADDeviceId")]
            public string AzureADDeviceId { get; set; }

            [JsonProperty("id")]
            public string Id { get; set; }
        }
    }
}
