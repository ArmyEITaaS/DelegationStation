using Microsoft.Extensions.Logging;

namespace ResetSyncedDevices
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
            ILogger<ResetSyncedDevices> logger = loggerFactory.CreateLogger<ResetSyncedDevices>();

            var resetter = new ResetSyncedDevices(loggerFactory);
            resetter.RunAsync().Wait();
        }
    }
}
