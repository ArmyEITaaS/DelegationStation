using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace CorporateIdentifierSync.Models
{
    public class CorpIDCounter
    {
        [Required]
        [JsonProperty(PropertyName = "id")]
        public Guid id { get; set; }
        public string PartitionKey { get; set; }
        public int CorpIDCount { get; set; }
        public int CorpIDReserve { get; set; }
        public DateTime CreatedDT { get; set; }
        public DateTime ModifiedDT { get; set; }

        public CorpIDCounter(int count) {
            id = Guid.NewGuid();
            PartitionKey = "CorpIDCounter";
            CorpIDCount = count;
            CorpIDReserve = 0;
            CreatedDT = DateTime.UtcNow;
            ModifiedDT = DateTime.UtcNow;
        }

        public override string ToString()
        {
            string output = $"CorpIDCount: {CorpIDCount}, CorpIDReserve: {CorpIDReserve}";
            return output;
        }

        public int GetTotal()
        {
            return CorpIDCount + CorpIDReserve;
        }

        [JsonProperty("_etag")]
        public string ETag { get; set; }
    }
}
