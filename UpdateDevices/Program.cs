using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UpdateDevices.Interfaces;
using UpdateDevices.Services;

namespace UpdateDevices
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var builder = FunctionsApplication.CreateBuilder(args);

            builder.ConfigureFunctionsWebApplication();
            builder.Logging.SetMinimumLevel(LogLevel.Debug);

            builder.Services.AddApplicationInsightsTelemetryWorkerService(options =>
            {
                options.EnableAdaptiveSampling = false;
            });
            builder.Services.ConfigureFunctionsApplicationInsights();
            builder.Services.AddSingleton<ICosmosDbService, CosmosDbService>();
            builder.Services.AddSingleton<IGraphBetaService, GraphBetaService>();
            builder.Services.AddSingleton<IGraphService, GraphService>();


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
