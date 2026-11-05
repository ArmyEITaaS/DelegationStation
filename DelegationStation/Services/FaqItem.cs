namespace DelegationStation.Services
{
    /// <summary>
    /// A single entry in the FAQ feed (wwwroot/faq.json).
    /// </summary>
    public class FaqItem
    {
        public string Question { get; set; } = string.Empty;
        public List<string> Answers { get; set; } = new();
    }
}
