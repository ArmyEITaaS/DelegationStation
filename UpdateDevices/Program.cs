using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
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
            services.AddSingleton<IGraphService, GraphService>();
          })
          .Build();

      host.Run();
    }

  }
}
