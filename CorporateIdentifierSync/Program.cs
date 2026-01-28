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
                .ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Debug);
                }).
                ConfigureServices(services =>
                {
                    services.AddApplicationInsightsTelemetryWorkerService();
                    services.ConfigureFunctionsApplicationInsights();
                    services.AddSingleton<ICosmosDbService, CosmosDbService>();
                    services.AddSingleton<IGraphService, GraphService>();
                    services.AddSingleton<IGraphBetaService, GraphBetaService>();
                })
                .Build();

            host.Run();
        }

    }
}
