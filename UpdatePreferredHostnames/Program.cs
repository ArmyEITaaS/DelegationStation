using Microsoft.Extensions.Logging;

namespace UpdatePreferredHostnames
{
    public class Program
    {
        static void Main(string[] args)
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.AddApplicationInsightsWebJobs(o => { o.ConnectionString = Environment.GetEnvironmentVariable("APPINSIGHTS_CONNECTION_STRING"); });
            });
            ILogger<UpdateHostnames> logger = loggerFactory.CreateLogger<UpdateHostnames>();


            var hostnameUpdater = new UpdateHostnames(loggerFactory);
            hostnameUpdater.RunAsync().Wait();

        }
    }
}