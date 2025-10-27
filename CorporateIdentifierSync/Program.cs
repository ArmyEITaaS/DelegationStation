using CorporateIdentifierSync.Interfaces;
using CorporateIdentifierSync.Services;
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

            // Application Insights isn't enabled by default. See https://aka.ms/AAt8mw4.
            // builder.Services
            //     .AddApplicationInsightsTelemetryWorkerService()
            //     .ConfigureFunctionsApplicationInsights();

            var host = new HostBuilder()
                .ConfigureFunctionsWebApplication()
                .ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Debug);
                }).
                ConfigureServices(services =>
                {
                    services.AddSingleton<ICosmosDbService, CosmosDbService>();
                    services.AddSingleton<IGraphService, GraphService>();
                    services.AddSingleton<IGraphBetaService, GraphBetaService>();
                })
                .Build();

            host.Run();
        }

    }
}
