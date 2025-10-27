using CorporateIdentifierSync.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta;
using System.Security.Cryptography.X509Certificates;
using Azure.Identity;
using DelegationStationShared;
using DelegationStationShared.Extensions;
using Microsoft.Graph.Beta.DeviceManagement.ImportedDeviceIdentities.ImportDeviceIdentityList;
using Microsoft.Graph.Beta.Models;
using Microsoft.Graph.Beta.Models.ODataErrors;


namespace CorporateIdentifierSync.Services
{
    public class GraphBetaService : IGraphBetaService
    {
        private readonly ILogger<GraphBetaService> _logger;
        private GraphServiceClient _graphClient;

        public GraphBetaService(IConfiguration configuration, ILogger<GraphBetaService> logger)
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
            string baseUrl = graphEndpoint + "beta";

            var certDN = configuration.GetSection("AzureAd:ClientCertificates:CertificateDistinguishedName").Value;

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


        public async Task<ImportedDeviceIdentity> AddCorporateIdentifier(ImportedDeviceIdentityType type, string identifier)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation($"Adding identifier: {identifier}", fullMethodName);

            ImportedDeviceIdentity importedDevice = new ImportedDeviceIdentity();
            importedDevice.ImportedDeviceIdentityType = type;
            importedDevice.ImportedDeviceIdentifier = identifier;

            List<ImportedDeviceIdentity> addList = new List<ImportedDeviceIdentity>();
            addList.Add(importedDevice);

            ImportDeviceIdentityListPostRequestBody requestBody = new ImportDeviceIdentityListPostRequestBody();
            requestBody.OverwriteImportedDeviceIdentities = false;
            requestBody.ImportedDeviceIdentities = addList;


            ImportedDeviceIdentity deviceIdentity;

            // Note:  If entry already exists, it will just return object
            var result = await _graphClient.DeviceManagement.ImportedDeviceIdentities.ImportDeviceIdentityList.PostAsImportDeviceIdentityListPostResponseAsync(requestBody);
            deviceIdentity = result.Value[0];
            _logger.DSLogInformation($"Identifier Added: {deviceIdentity.ImportedDeviceIdentifier}", fullMethodName);

            return deviceIdentity;

        }

        public async Task<bool> CorporateIdentifierExists(string identifierId)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation($"Checking for identifier: {identifierId}", fullMethodName);

            if (string.IsNullOrEmpty(identifierId))
            {
                return false;
            }

            ImportedDeviceIdentity deviceIdentity = null;
            try
            {
                deviceIdentity = await _graphClient.DeviceManagement.ImportedDeviceIdentities[identifierId].GetAsync();
                _logger.DSLogInformation($"Identifier Found: {identifierId}", fullMethodName);
                return true;
            }
            catch (ODataError odataError) when (odataError.ResponseStatusCode == 404)
            {
                // This is the error returned when it tries to delete an object that's not found
                // Return true since it's already not present
                _logger.DSLogInformation($"Device corporate identifier {identifierId} not found in Graph", fullMethodName);
                return false;
            }
            catch (Exception ex)
            {
                _logger.DSLogError($"Unable to query device identifier {identifierId} from Graph: " + ex, fullMethodName);
                return false;
            }
        }

        public async Task<bool> DeleteCorporateIdentifier(string ID)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation($"Deleting identifier: {ID}", fullMethodName);

            try
            {
                await _graphClient.DeviceManagement.ImportedDeviceIdentities[ID].DeleteAsync();
                _logger.DSLogInformation($"Identifier Deleted: {ID}", fullMethodName);
                return true;
            }
            catch (ODataError odataError) when (odataError.Error.Code.Equals("BadRequest"))
            {
                // This is the error returned when it tries to delete an object that's not found
                // Return true since it's already not present
                _logger.DSLogInformation($"Device corporate identifier {ID} not found in Graph.", fullMethodName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.DSLogError($"Unable to delete device identifier {ID} from Graph: " + ex, fullMethodName);
                return false;
            }
        }

    }
}
