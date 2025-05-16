using UpdateDevices.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta;
using System.Security.Cryptography.X509Certificates;
using Azure.Identity;
using DelegationStationShared;
using DelegationStationShared.Extensions;
using Microsoft.Graph.Beta.DeviceManagement.ManagedDevices.Item.SetDeviceName;
using Microsoft.Graph.Beta.Models;
using Microsoft.Graph.Beta.Models.ODataErrors;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;


namespace UpdateDevices.Services
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

        public async Task<bool> SetDeviceName(string managedDeviceID, string newHostName)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation($"Setting Device Name for Managed Device {managedDeviceID} to {newHostName}", fullMethodName);
            var success = false;
            SetDeviceNamePostRequestBody requestBody = new SetDeviceNamePostRequestBody();
            requestBody.DeviceName = newHostName;

            try
            {
                await _graphClient.DeviceManagement.ManagedDevices[managedDeviceID].SetDeviceName.PostAsync(requestBody);
                success = true;
            }
            catch (Exception ex)
            {
                _logger.DSLogException($"Unable to rename device ID (possible cause--not a company device): {managedDeviceID}", ex, fullMethodName);
            }
            return success;
        }
    }
}
