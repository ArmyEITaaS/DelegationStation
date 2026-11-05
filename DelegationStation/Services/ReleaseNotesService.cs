using System.Text.Json;

namespace DelegationStation.Services
{
    public interface IReleaseNotesService
    {
        /// <summary>
        /// Release notes ordered newest-first, as they appear in release-notes.json.
        /// </summary>
        IReadOnlyList<ReleaseNote> ReleaseNotes { get; }

        /// <summary>
        /// The version of the most recent release note entry. Used to determine
        /// whether a user has already viewed the latest updates.
        /// </summary>
        string CurrentVersion { get; }
    }

    /// <summary>
    /// Loads release notes from wwwroot/release-notes.json so new releases can be
    /// documented by editing that file instead of the Help page markup.
    /// </summary>
    public class ReleaseNotesService : IReleaseNotesService
    {
        private const string ReleaseNotesFileName = "release-notes.json";

        public IReadOnlyList<ReleaseNote> ReleaseNotes { get; }

        public string CurrentVersion => ReleaseNotes.Count > 0 ? ReleaseNotes[0].Version : "0";

        public ReleaseNotesService(IWebHostEnvironment env, ILogger<ReleaseNotesService> logger)
        {
            var path = Path.Combine(env.WebRootPath, ReleaseNotesFileName);

            try
            {
                var json = File.ReadAllText(path);
                ReleaseNotes = JsonSerializer.Deserialize<List<ReleaseNote>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new List<ReleaseNote>();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load release notes from {Path}.", path);
                ReleaseNotes = new List<ReleaseNote>();
            }
        }
    }
}
