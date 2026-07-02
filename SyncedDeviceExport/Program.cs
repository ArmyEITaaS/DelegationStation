using Microsoft.Extensions.Logging;

namespace SyncedDeviceExport
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
            ILogger<SyncedDeviceExportJob> logger = loggerFactory.CreateLogger<SyncedDeviceExportJob>();

            var job = new SyncedDeviceExportJob(loggerFactory);
            job.RunAsync().Wait();
        }
    }
}
