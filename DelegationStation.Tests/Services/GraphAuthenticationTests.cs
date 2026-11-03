using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DelegationStation.Tests.Services
{
    [TestClass]
    public class GraphAuthenticationTests
    {
        private static readonly Type[] GraphServiceTypes =
        [
            typeof(DelegationStation.Services.GraphService),
            typeof(UpdateDevices.Services.GraphService),
            typeof(UpdateDevices.Services.GraphBetaService),
            typeof(CorporateIdentifierSync.Services.GraphService),
            typeof(CorporateIdentifierSync.Services.GraphBetaService)
        ];

        [TestMethod]
        [DynamicData(nameof(AuthenticationCases))]
        public void GraphAuthenticationUsesAppCredentialsForDevelopmentOrDisabledManagedIdentity(
            Type serviceType, string environment, string? useAppCredentials, string expectedMessage)
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                EnvironmentName = environment
            });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureEnvironment"] = "AzurePublicCloud",
                ["GraphEndpoint"] = "https://graph.microsoft.com/",
                ["AzureAd:TenantId"] = "00000000-0000-0000-0000-000000000001",
                ["AzureAd:ClientId"] = "00000000-0000-0000-0000-000000000002",
                ["AzureApp:ClientSecret"] = "test-only-not-a-real-secret",
                ["AzureAd:ClientCertificates:CertificateDistinguishedName"] = null,
                ["CertificateDistinguishedName"] = null,
                ["GRAPH_USE_APP_CREDENTIALS"] = useAppCredentials
            });
            var loggerProvider = new CapturingLoggerProvider();
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(loggerProvider);
            builder.Services.AddTransient(serviceType);
            using var host = builder.Build();

            var service = host.Services.GetRequiredService(serviceType);

            Assert.IsNotNull(service);
            Assert.AreEqual(environment, host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
            Assert.IsTrue(loggerProvider.Messages.Any(message => message.Contains(expectedMessage)),
                $"Expected {serviceType.FullName} in {environment} with GRAPH_USE_APP_CREDENTIALS={useAppCredentials ?? "<unset>"} to log '{expectedMessage}'.");
            string unexpectedMessage = expectedMessage.Contains("Client Secret")
                ? "Using Managed Identity"
                : "Using Client Secret";
            Assert.IsFalse(loggerProvider.Messages.Any(message => message.Contains(unexpectedMessage)));
        }

        public static IEnumerable<object?[]> AuthenticationCases()
        {
            foreach (var serviceType in GraphServiceTypes)
            {
                foreach (string environment in new[] { Environments.Production, Environments.Staging, Environments.Development })
                {
                    foreach (string? value in new string?[] { null, "false", "true", "TRUE" })
                    {
                        string expectedMessage = environment == Environments.Development
                            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                            ? "Using Client Secret for Graph service"
                            : "Using Managed Identity to authenticate to Graph service";
                        yield return new object?[] { serviceType, environment, value, expectedMessage };
                    }
                }
            }
        }

        private sealed class CapturingLoggerProvider : ILoggerProvider
        {
            public List<string> Messages { get; } = [];

            public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

            public void Dispose() { }
        }

        private sealed class CapturingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                messages.Add(formatter(state, exception));
            }
        }
    }
}
