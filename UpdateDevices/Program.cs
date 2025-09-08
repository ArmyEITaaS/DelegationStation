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

      var host = new HostBuilder()
          .ConfigureFunctionsWebApplication()
          .ConfigureLogging(logging =>
          {
            logging.SetMinimumLevel(LogLevel.Debug);
          }).
          ConfigureServices(services =>
          {
            services.AddSingleton<ICosmosDbService, CosmosDbService>();
            services.AddSingleton<IGraphBetaService, GraphBetaService>();
            services.AddSingleton<IGraphService, GraphService>();
          })
          .Build();

      host.Run();
    }

  }
}
