using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Reflection;
using System.Text;

namespace IntuneEnrollment.Utilities
{
    public class GraphAccess
    {
        public static async Task<String> GetAccessTokenAsync(string uri, ILogger logger)
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
                logger.LogError(sb.ToString());
                throw new ArgumentNullException(sb.ToString());
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
                logger.LogError($"{fullMethodName} Error: getting access token for URI {tokenUri}: {ex.Message}");
                throw new InvalidOperationException($"Error: getting access token for URI {tokenUri}: {ex.Message}");
            }
        }
    }
}
