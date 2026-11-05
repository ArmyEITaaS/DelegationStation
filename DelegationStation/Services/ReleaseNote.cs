namespace DelegationStation.Services
{
    /// <summary>
    /// A single entry in the release notes feed (wwwroot/release-notes.json).
    /// </summary>
    public class ReleaseNote
    {
        public string Version { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public List<string> Changes { get; set; } = new();
    }
}
