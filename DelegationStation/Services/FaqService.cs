using System.Text.Json;

namespace DelegationStation.Services
{
    public interface IFaqService
    {
        /// <summary>
        /// FAQ entries in the order they appear in faq.json.
        /// </summary>
        IReadOnlyList<FaqItem> FaqItems { get; }
    }

    /// <summary>
    /// Loads FAQ entries from wwwroot/faq.json so questions can be added or edited
    /// by editing that file instead of the Help page markup.
    /// </summary>
    public class FaqService : IFaqService
    {
        private const string FaqFileName = "faq.json";

        public IReadOnlyList<FaqItem> FaqItems { get; }

        public FaqService(IWebHostEnvironment env, ILogger<FaqService> logger)
        {
            var path = Path.Combine(env.WebRootPath, FaqFileName);

            try
            {
                var json = File.ReadAllText(path);
                FaqItems = JsonSerializer.Deserialize<List<FaqItem>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new List<FaqItem>();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load FAQ items from {Path}.", path);
                FaqItems = new List<FaqItem>();
            }
        }
    }
}
