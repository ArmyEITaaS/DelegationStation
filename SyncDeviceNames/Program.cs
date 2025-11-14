using Microsoft.Extensions.Logging;

namespace SyncDeviceNames
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
            ILogger<SyncDeviceNamesJob> logger = loggerFactory.CreateLogger<SyncDeviceNamesJob>();

            SyncDeviceNamesJob syncJob = new SyncDeviceNamesJob(loggerFactory);
            syncJob.RunAsync().Wait();
        }
    }



}

