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
            var builder = FunctionsApplication.CreateBuilder(args);

            builder.ConfigureFunctionsWebApplication();

            builder.Services.AddApplicationInsightsTelemetryWorkerService(options =>
            {
                options.EnableAdaptiveSampling = false;
            });
            builder.Services.ConfigureFunctionsApplicationInsights();
            builder.Services.AddSingleton<ICosmosDbService, CosmosDbService>();
            builder.Services.AddSingleton<IGraphService, GraphService>();
            builder.Services.AddSingleton<IGraphBetaService, GraphBetaService>();
            builder.Services.AddSingleton<IFunctionSingletonLock, BlobLeaseSingletonLock>();

            // disables the default that only logs warnings and above to Application Insights
            builder.Logging.Services.Configure<LoggerFilterOptions>(options =>
            {
                LoggerFilterRule defaultRule = options.Rules.FirstOrDefault(rule => rule.ProviderName
                    == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
                if (defaultRule is not null)
                {
                    options.Rules.Remove(defaultRule);
                }
            });

            var host = builder.Build();
            host.Run();
        }

    }
}
