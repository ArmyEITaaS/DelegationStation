using Azure.Identity;
using UpdatePreferredHostnames.Interfaces;
using DelegationStationShared;
using DelegationStationShared.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using DelegationStationShared.Models;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Text.RegularExpressions;


namespace UpdatePreferredHostnames.Services
{
    public class GraphService : IGraphService
    {

        private static string _guidRegex = "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$";

        private readonly ILogger<GraphService> _logger;
        private GraphServiceClient _graphClient;

        public GraphService(IConfiguration configuration, ILogger<GraphService> logger)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            this._logger = logger;

            var azureCloud = configuration.GetSection("AzureEnvironment").Value;
            var graphEndpoint = configuration.GetSection("GraphEndpoint").Value;

            var options = new TokenCredentialOptions
            {
                AuthorityHost = azureCloud == "AzurePublicCloud" ? AzureAuthorityHosts.AzurePublicCloud : AzureAuthorityHosts.AzureGovernment
            };

            var scopes = new string[] { $"{graphEndpoint}.default" };
            string baseUrl = graphEndpoint + "v1.0";

            var certDN = configuration.GetSection("CertificateDistinguishedName").Value;

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
                    configuration.GetSection("AzureAd:TenantId").Value,
                    configuration.GetSection("AzureAd:ClientId").Value,
                    certificate,
                    options
                );
                store.Close();
                this._graphClient = new GraphServiceClient(clientCertCredential, scopes, baseUrl);
            }
            else
            {
                _logger.DSLogInformation("Using Client Secret for Graph service", fullMethodName);
                _logger.DSLogDebug("AzureCloud: " + azureCloud, fullMethodName);
                _logger.DSLogDebug("GraphEndpoint: " + graphEndpoint, fullMethodName);


                var clientSecretCredential = new ClientSecretCredential(
                    configuration.GetSection("AzureAd:TenantId").Value,
                    configuration.GetSection("AzureAd:ClientId").Value,
                    configuration.GetSection("AzureApp:ClientSecret").Value,
                    options
                );

                this._graphClient = new GraphServiceClient(clientSecretCredential, scopes, baseUrl);
            }
        }

        public async Task AddDeviceToAzureADGroup(string deviceId, string deviceObjectId, DeviceUpdateAction group)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            var graphEndpoint = Environment.GetEnvironmentVariable("GraphEndpoint", EnvironmentVariableTarget.Process);

            string groupId = group.Value;

            if (string.IsNullOrEmpty(deviceObjectId))
            {
                _logger.DSLogError("DeviceObjectId is null or empty.", fullMethodName);
                return;
            }
            if (string.IsNullOrEmpty(groupId))
            {
                _logger.DSLogError("GroupId is null or empty.", fullMethodName);
                return;
            }
            if (Regex.IsMatch(deviceObjectId, _guidRegex) == false)
            {
                _logger.DSLogError("DeviceId is not a valid GUID. DeviceId: " + deviceId, fullMethodName);
                return;
            }
            if (Regex.IsMatch(groupId, _guidRegex) == false)
            {
                _logger.DSLogError("GroupId is not a valid GUID. GroupId: " + groupId + "", fullMethodName);
                return;
            }

            try
            {
                var requestBody = new ReferenceCreate
                {
                    OdataId = $"{graphEndpoint}v1.0/devices/{deviceObjectId}"
                };
                await _graphClient.Groups[$"{groupId}"].Members.Ref.PostAsync(requestBody);
                _logger.DSLogAudit("Added DeviceId " + deviceId + " (as Object ID " + deviceObjectId + ") to Group " + group.Name + " ( " + groupId + ").", fullMethodName);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("already exist"))
                {
                    _logger.DSLogInformation("DeviceId " + deviceId + " (as Object ID " + deviceObjectId + ") already exists in Group " + group.Name + " (" + groupId + ").", fullMethodName);
                }
                else
                {
                    _logger.DSLogException("Unable to add DeviceId " + deviceId + " (as Object ID " + deviceObjectId + ") to Group " + group.Name + " (" + groupId + ").", ex, fullMethodName);
                }
            }
        }

        public async Task AddDeviceToAzureAdministrativeUnit(string deviceId, string deviceObjectId, DeviceUpdateAction adminUnit)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            var graphEndpoint = Environment.GetEnvironmentVariable("GraphEndpoint", EnvironmentVariableTarget.Process);
            string auId = adminUnit.Value;

            if (string.IsNullOrEmpty(deviceObjectId))
            {
                _logger.DSLogError("Device Object Id is null or empty.", fullMethodName);
                return;
            }
            if (string.IsNullOrEmpty(auId))
            {
                _logger.DSLogError("AU Id is null or empty.", fullMethodName);
                return;
            }
            if (Regex.IsMatch(deviceId, _guidRegex) == false)
            {
                _logger.DSLogError("Device Object Id is not a valid GUID. DeviceId: " + deviceObjectId + "", fullMethodName);
                return;
            }
            if (Regex.IsMatch(auId, _guidRegex) == false)
            {
                _logger.DSLogError("AU Id is not a valid GUID. AU Id: " + auId, fullMethodName);
                return;
            }

            try
            {
                var requestBody = new ReferenceCreate
                {
                    OdataId = $"{graphEndpoint}v1.0/devices/{deviceObjectId}"
                };
                await _graphClient.Directory.AdministrativeUnits[$"{auId}"].Members.Ref.PostAsync(requestBody);
                _logger.DSLogAudit("Added Device " + deviceId + " (as Object ID " + deviceObjectId + ") to Administrative Unit " + adminUnit.Name + " (" + auId + ").", fullMethodName);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("conflicting object"))
                {
                    _logger.DSLogInformation("Device " + deviceId + " (as Object ID " + deviceObjectId + ") already exists in Administrative Unit " + adminUnit.Name + " (" + auId + ").", fullMethodName);
                }
                else
                {
                    _logger.DSLogException("Unable to add Device " + deviceId + " (as Object ID " + deviceObjectId + ") to Administrative Unit: " + adminUnit.Name + " (" + auId + ").", ex, fullMethodName);
                }
            }
        }

        public async Task<string> GetDeviceObjectID(string azureADDeviceID)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            string deviceObjectID = null;
            try
            {

                var deviceObj = await _graphClient.Devices.GetAsync((requestConfiguration) =>
                {
                    requestConfiguration.QueryParameters.Filter = $"deviceId eq '{azureADDeviceID}'";
                    requestConfiguration.QueryParameters.Select = new string[] { "id" };
                });
                deviceObjectID = deviceObj.Value.FirstOrDefault().Id;

            }
            catch (Exception ex)
            {
                _logger.DSLogException("Failed to retrieve graph device ID using .\n", ex, fullMethodName);
            }

            return deviceObjectID;
        }

        public async Task<List<ManagedDevice>> GetNewDeviceManagementObjectsAsync(DateTime dateTime)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Getting devices enrolled since " + dateTime.ToString("MM/dd/yyyy HH:mm:ss") + " UTC", fullMethodName);


            List<ManagedDevice> devices = new List<ManagedDevice>();

            try
            {
                var managedDevices = await _graphClient.DeviceManagement.ManagedDevices
                    .GetAsync((requestConfiguration) =>
                    {
                        requestConfiguration.QueryParameters.Select = ["id", "manufacturer", "model", "serialNumber", "azureADDeviceId", "userPrincipalName", "enrolledDateTime"];
                        requestConfiguration.QueryParameters.Filter = $"enrolledDateTime ge {dateTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")}";
                    });

                var pageIterator = PageIterator<ManagedDevice, ManagedDeviceCollectionResponse>
                    .CreatePageIterator(_graphClient, managedDevices, (device) =>
                    {
                        devices.Add(device);
                        return true;
                    });

                await pageIterator.IterateAsync();

                _logger.DSLogInformation("Found " + devices.Count + " new enrolled devices since " + dateTime.ToString("MM/dd/yyyy HH:mm:ss") + " UTC", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Unable to retrieve managed devices from Graph", ex, fullMethodName);
            }

            return devices;
        }

        public async Task UpdateAttributesOnDeviceAsync(string deviceId, string objectDeviceId, List<DeviceUpdateAction> updateActions)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            if (string.IsNullOrEmpty(deviceId) || updateActions == null)
            {
                _logger.DSLogError("DeviceId or updateActions is null or empty. DeviceId: " + objectDeviceId, fullMethodName);
                return;
            }
            if (Regex.IsMatch(deviceId, _guidRegex) == false)
            {
                _logger.DSLogError("DeviceId is not a valid GUID. DeviceId: " + objectDeviceId, fullMethodName);
                return;
            }
            if (updateActions.Count < 1)
            {
                _logger.DSLogWarning("No update actions configured for " + objectDeviceId, fullMethodName);
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

            try
            {
                var result = await _graphClient.Devices[$"{objectDeviceId}"].PatchAsync(requestBody);
                _logger.DSLogAudit("Applied Attributes to Device " + deviceId + ": [" + string.Join(", ", updateActions.Select(a => a.Name + ": " + a.Value)) + "]", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Unable to apply ExtensionAttributes to DeviceId " + deviceId + ": [" + string.Join(", ", updateActions.Select(a => a.Name + ": " + a.Value)) + "]", ex, fullMethodName);
            }
        }



        public async Task<ManagedDevice> GetManagedDevice(string make, string model, string serialNum)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            ManagedDevice result = null;
            try
            {
                var devices = await _graphClient.DeviceManagement.ManagedDevices.GetAsync((requestConfiguration) =>
                {
                    requestConfiguration.QueryParameters.Filter = $"(manufacturer eq '{make}' and model eq '{model}' and serialNumber eq '{serialNum}')";
                });
                if (devices == null)
                {
                    return null;
                }
                else
                {
                    result = devices.Value.FirstOrDefault();
                }
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError err) when (err.Error.Code.Equals("ResourceNotFound"))
            {
                _logger.DSLogInformation($"No managed device found for: {make} {model} {serialNum}", fullMethodName);
                return null;
            }

            return result;

        }

        public async Task<ManagedDevice> GetManagedDevice(string deviceID)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            ManagedDevice result = null;

            try
            {
                result = await _graphClient.DeviceManagement.ManagedDevices[deviceID].GetAsync();
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError err) when (err.Error.Code.Equals("ResourceNotFound"))
            {
                _logger.DSLogInformation($"No managed device found for ID: {deviceID}", fullMethodName);
                return null;
            }

            return result;

        }
    }
}
