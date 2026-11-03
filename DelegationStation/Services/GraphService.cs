using Azure.Identity;
using DelegationStation.Interfaces;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System.Security.Cryptography.X509Certificates;

namespace DelegationStation.Services
{
    public class GraphService : IGraphService
    {
        private readonly ILogger<GraphService> _logger;
        private GraphServiceClient _graphClient;

        public GraphService(IConfiguration configuration, ILogger<GraphService> logger, IHostEnvironment env)
        {
            this._logger = logger;

            var azureCloud = configuration.GetSection("AzureEnvironment").Value;
            var graphEndpoint = configuration.GetSection("GraphEndpoint").Value;

            var options = new TokenCredentialOptions
            {
                AuthorityHost = azureCloud == "AzurePublicCloud" ? AzureAuthorityHosts.AzurePublicCloud : AzureAuthorityHosts.AzureGovernment
            };

            var scopes = new string[] { $"{graphEndpoint}.default" };
            string baseUrl = graphEndpoint + "v1.0";

            if (env.IsDevelopment() || configuration.GetValue<bool>("GRAPH_USE_APP_CREDENTIALS"))
            {

                var certDN = configuration.GetSection("AzureAd:ClientCertificates:CertificateDistinguishedName").Value;

                if (!String.IsNullOrEmpty(certDN))
                {
                    _logger.LogInformation("Using certificate authentication: ");
                    _logger.LogDebug("AzureCloud: " + azureCloud);
                    _logger.LogDebug("GraphEndpoint: " + graphEndpoint);

                    X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                    store.Open(OpenFlags.ReadOnly);
                    _logger.LogInformation("Using certificate with Subject Name {0} for Graph service", certDN);
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
                    _logger.LogInformation("Using Client Secret for Graph service");
                    _logger.LogDebug("AzureCloud: " + azureCloud);
                    _logger.LogDebug("GraphEndpoint: " + graphEndpoint);


                    var clientSecretCredential = new ClientSecretCredential(
                        configuration.GetSection("AzureAd:TenantId").Value,
                        configuration.GetSection("AzureAd:ClientId").Value,
                        configuration.GetSection("AzureApp:ClientSecret").Value,
                        options
                    );

                    this._graphClient = new GraphServiceClient(clientSecretCredential, scopes, baseUrl);
                }
            }
            else
            {
                _logger.LogInformation("Using Managed Identity to authenticate to Graph service");
                _logger.LogDebug("AzureCloud: " + azureCloud);
                _logger.LogDebug("GraphEndpoint: " + graphEndpoint);

                ManagedIdentityCredential managedIdentityCredential = new ManagedIdentityCredential(options: options);
                this._graphClient = new GraphServiceClient(managedIdentityCredential, scopes, baseUrl);


            }
        }

        public async Task<string> GetSecurityGroupName(string groupId)
        {
            if (groupId == null)
            {
                throw new Exception("GraphService GetSecurityGroupName was sent null Group Id");
            }

            if (!System.Text.RegularExpressions.Regex.Match(groupId, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success)
            {
                throw new Exception($"GraphService GetSecurityGroupName groupId did not match GUID format {groupId}");
            }

            var group = await _graphClient.Groups[groupId].GetAsync();
            string name = group?.DisplayName ?? "";
            return name;
        }

        public async Task<List<Group>> SearchGroupAsync(string query)
        {
            if (query == null)
            {
                throw new Exception("GraphService SearchGroupAsync was sent null query");
            }

            if (System.Text.RegularExpressions.Regex.Match(query, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success)
            {
                var groupsById = await _graphClient.Groups.GetAsync((requestConfiguration) =>
                {
                    requestConfiguration.QueryParameters.Filter = $"id eq {query}";
                    requestConfiguration.QueryParameters.Orderby = new string[] { "displayName" };
                    requestConfiguration.QueryParameters.Select = new string[] { "id", "displayName", "description" };
                    requestConfiguration.QueryParameters.Top = 5;
                    requestConfiguration.Headers.Add("ConsistencyLevel", "eventual");
                });

                if (groupsById == null)
                {
                    return new List<Group>();
                }

                return groupsById.Value ?? new List<Group>();
            }

            var groups = await _graphClient.Groups.GetAsync((requestConfiguration) =>
            {
                requestConfiguration.QueryParameters.Search = $"\"displayName:{query}\" OR \"description:{query}\"";
                requestConfiguration.QueryParameters.Orderby = new string[] { "displayName" };
                requestConfiguration.QueryParameters.Select = new string[] { "id", "displayName", "description" };
                requestConfiguration.QueryParameters.Top = 5;
                requestConfiguration.Headers.Add("ConsistencyLevel", "eventual");
            });

            if (groups == null)
            {
                return new List<Group>();
            }
            return groups.Value ?? new List<Group>();
        }

        public async Task<List<AdministrativeUnit>> SearchAdministrativeUnitAsync(string query)
        {
            if (query == null)
            {
                throw new Exception("GraphService SearchAdministrativeUnitAsync was sent null query");
            }

            var administrativeUnits = await _graphClient.Directory.AdministrativeUnits.GetAsync((requestConfiguration) =>
            {
                requestConfiguration.QueryParameters.Search = $"\"displayName:{query}\" OR \"description:{query}\"";
                requestConfiguration.QueryParameters.Orderby = new string[] { "displayName" };
                requestConfiguration.QueryParameters.Select = new string[] { "id", "displayName", "description" };
                requestConfiguration.QueryParameters.Top = 5;
                requestConfiguration.Headers.Add("ConsistencyLevel", "eventual");
            });
            if (administrativeUnits == null)
            {
                return new List<AdministrativeUnit>();
            }
            return administrativeUnits.Value ?? new List<AdministrativeUnit>();
        }
    }
}
