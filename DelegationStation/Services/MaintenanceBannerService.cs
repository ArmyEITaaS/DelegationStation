namespace DelegationStation.Services
{
    /// <summary>
    /// The recognized color themes for the maintenance banner. Each theme is a
    /// light background paired with a matching dark text/border color, similar
    /// to Bootstrap's alert styles, so custom colors stay readable and on-brand
    /// instead of requiring callers to pick their own foreground/background pair.
    /// </summary>
    public enum MaintenanceBannerTheme
    {
        Amber,
        Red,
        Green,
        Blue
    }

    /// <summary>
    /// The parsed contents of the maintenance banner file: the message to
    /// display and the color theme used to draw attention to it.
    /// </summary>
    public record MaintenanceBannerContent(string Message, MaintenanceBannerTheme Theme);

    public interface IMaintenanceBannerService
    {
        /// <summary>
        /// Returns the current maintenance banner, or null if
        /// wwwroot/maintenance-banner.txt does not exist or has no message.
        /// The file is read on every call so an admin can create, edit, or
        /// remove it without restarting the app.
        /// </summary>
        MaintenanceBannerContent? GetBanner();
    }

    /// <summary>
    /// Displays a site-wide banner sourced from wwwroot/maintenance-banner.txt.
    /// The banner is only shown while that file is present, so operators can
    /// announce maintenance windows or other notices by adding the file and
    /// clear them by deleting it - no deployment or restart required.
    ///
    /// The file's first line may optionally name a color theme ("red", "green",
    /// or "blue", case-insensitive) to draw attention to the banner; the
    /// remaining lines are the message. If the first line does not name a
    /// recognized theme, the entire file is treated as the message and the
    /// default amber theme is used.
    /// </summary>
    public class MaintenanceBannerService : IMaintenanceBannerService
    {
        private const string BannerFileName = "maintenance-banner.txt";

        private readonly string _path;
        private readonly ILogger<MaintenanceBannerService> _logger;

        public MaintenanceBannerService(IWebHostEnvironment env, ILogger<MaintenanceBannerService> logger)
        {
            _path = Path.Combine(env.WebRootPath, BannerFileName);
            _logger = logger;
        }

        public MaintenanceBannerContent? GetBanner()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return null;
                }

                var lines = File.ReadAllLines(_path);
                return Parse(lines);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read maintenance banner from {Path}.", _path);
                return null;
            }
        }

        public static MaintenanceBannerContent? Parse(string[] lines)
        {
            var theme = MaintenanceBannerTheme.Amber;
            var messageLines = lines;

            if (lines.Length > 0 && TryParseTheme(lines[0].Trim(), out var parsedTheme))
            {
                theme = parsedTheme;
                messageLines = lines.Skip(1).ToArray();
            }

            var message = string.Join("\n", messageLines).Trim();
            return string.IsNullOrEmpty(message) ? null : new MaintenanceBannerContent(message, theme);
        }

        private static bool TryParseTheme(string firstLine, out MaintenanceBannerTheme theme)
        {
            switch (firstLine.ToLowerInvariant())
            {
                case "red":
                    theme = MaintenanceBannerTheme.Red;
                    return true;
                case "green":
                    theme = MaintenanceBannerTheme.Green;
                    return true;
                case "blue":
                    theme = MaintenanceBannerTheme.Blue;
                    return true;
                default:
                    theme = MaintenanceBannerTheme.Amber;
                    return false;
            }
        }
    }
}


