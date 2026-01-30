using CorporateIdentifierSync.Interfaces;
using CorporateIdentifierSync.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


namespace CorporateIdentifierSync
{
    internal class Program
    {
        static void Main(string[] args)
        {

            var host = new HostBuilder()
                .ConfigureFunctionsWebApplication()
                .ConfigureServices(services =>
                {
                    services.AddApplicationInsightsTelemetryWorkerService();
                    services.ConfigureFunctionsApplicationInsights();
                    services.AddSingleton<ICosmosDbService, CosmosDbService>();
                    services.AddSingleton<IGraphService, GraphService>();
                    services.AddSingleton<IGraphBetaService, GraphBetaService>();
                })
                .ConfigureLogging(logging =>
                {
                    //disables the default that only logs warnings and above to Application Insights
                    logging.Services.Configure<LoggerFilterOptions>(options =>
                    {
                        LoggerFilterRule defaultRule = options.Rules.FirstOrDefault(rule => rule.ProviderName
                            == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
                        if (defaultRule is not null)
                        {
                            options.Rules.Remove(defaultRule);
                        }
                    });
                })
                .Build();

            host.Run();
        }

    }
}
