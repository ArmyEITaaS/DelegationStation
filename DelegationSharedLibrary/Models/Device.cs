using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace DelegationStationShared.Models
{
    public class Device
    {
        [Required]
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "Make is Required")]
        [RegularExpression(@"^[a-zA-Z0-9\-_.,&\(\)\s]+$", ErrorMessage = "Only use letters, numbers, or the following special characters: -_&().,")]
        public string Make { get; set; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "Model is Required")]

        [RegularExpression(@"^[a-zA-Z0-9\-_.,&\(\)\s]+$", ErrorMessage = "Only use letters, numbers, or the following special characters: -_&().,")]
        public string Model { get; set; }


        [Required(AllowEmptyStrings = false, ErrorMessage = "Serial Number is Required")]
        [RegularExpression(@"^[a-zA-Z0-9\-_.\s]+$", ErrorMessage = "Use letters, numbers, -, _, or . for SerialNumber value.")]
        public string SerialNumber { get; set; }

        [RegularExpression(@"^([a-fA-F0-9]{2}[:-]){5}([a-fA-F0-9]{2})$", ErrorMessage = "MAC address must use : or -  and be 12 numbers or letters A - F to match the IEEE 802 format")]
        public string MacAddress { get; set; }
        public string PartitionKey { get; set; }

        public List<string> Tags { get; set; }
        [Required(AllowEmptyStrings = false)]
        public string Type { get; private set; }

        public DateTime ModifiedUTC { get; set; }

        public string? AddedBy { get; set; }
        public bool Update { get; set; }
        public List<DeviceUpdateAction> UpdateActions { get; set; }

        public Device()
        {
            Id = Guid.NewGuid();
            PartitionKey = this.Id.ToString();
            Make = string.Empty;
            Model = string.Empty;
            SerialNumber = string.Empty;
            MacAddress = string.Empty;
            Tags = new List<string>();
            UpdateActions = new List<DeviceUpdateAction>();
            Type = typeof(Device).Name;
            ModifiedUTC = DateTime.UtcNow;
        }

        public Device(string make, string model, string serialNumber, string macAddress, List<string> tags)
        {
            Id = Guid.NewGuid();
            PartitionKey = this.Id.ToString();
            Make = make;
            Model = model;
            SerialNumber = serialNumber;
            MacAddress = macAddress;
            Tags = tags;
            UpdateActions = new List<DeviceUpdateAction>();
            Type = typeof(Device).Name;
            ModifiedUTC = DateTime.UtcNow;
        }
    }
}
