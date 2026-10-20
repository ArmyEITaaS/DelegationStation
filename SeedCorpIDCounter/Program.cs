using Microsoft.Extensions.Logging;

namespace SeedCorpIDCounter
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
            ILogger<SeedCorpIDCounterJob> logger = loggerFactory.CreateLogger<SeedCorpIDCounterJob>();

            string? corpIdCountString = Environment.GetEnvironmentVariable("CORPID_INITIAL_COUNT");
            if (!int.TryParse(corpIdCountString, out int corpIdCount) || corpIdCount < 0)
            {
                logger.LogError("CORPID_INITIAL_COUNT environment variable is not set or is not a valid non-negative integer. Exiting.");
                Environment.Exit(1);
            }

            var job = new SeedCorpIDCounterJob(loggerFactory);
            job.RunAsync(corpIdCount).Wait();
        }
    }
}
