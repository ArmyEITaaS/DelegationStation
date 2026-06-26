using Microsoft.Extensions.Logging;

namespace DeviceAudit
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
            ILogger<DeviceAudit> logger = loggerFactory.CreateLogger<DeviceAudit>();

            var auditor = new DeviceAudit(loggerFactory);
            auditor.RunAsync().Wait();
        }
    }
}
