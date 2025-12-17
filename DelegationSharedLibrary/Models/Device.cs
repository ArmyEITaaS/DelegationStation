using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using DelegationStationShared.Enums;

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
        [RegularExpression(@"^[a-zA-Z0-9\-_.,&\(\)+\s]+$", ErrorMessage = "Only use letters, numbers, or the following special characters: -_&().+,")]
        public string Model { get; set; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "Serial Number is Required")]
        [RegularExpression(@"^[a-zA-Z0-9\-_.\s]+$", ErrorMessage = "Only use letters, numbers, -, _, or . for SerialNumber value.")]
        public string SerialNumber { get; set; }

        // Validation applicable to all devices done here
        // Validation for specific tags handled in customer validation class
        [RegularExpression(@"^$|^[0-9a-zA-Z](?:[0-9a-zA-Z-]*[0-9a-zA-Z])?$", ErrorMessage = "Only use letters, numbers, or hyphen for Preferred Hostname value. Hyphens may not be at beginning or end.")]
        [MaxLength(15, ErrorMessage = "Preferred Hostname cannot exceed 15 characters.")]
        public string PreferredHostname { get; set; }

        public string PartitionKey { get; set; }

        // More than one tag is not currently supported
        public List<string> Tags { get; set; }
        [Required(AllowEmptyStrings = false)]
        public string Type { get; private set; }

        // Note:  Because we don't allow modifications from GUI is really AddedUTC
        public DateTime ModifiedUTC { get; set; }

        // GUID of user who created device in webapp
        public string? AddedBy { get; set; }

        // Corporate Identifier related
        public string CorporateIdentity { get; set; }
        public DateTime LastCorpIdentitySync { get; set; }
        public string CorporateIdentityID { get; set; }
        public DeviceStatus Status { get; set; }

        [Required]
        public DeviceOS? OS { get; set; }
        public DateTime? MarkedToDeleteUTC { get; set; }


        //NOTE:  The following settings are currently unused
        [RegularExpression(@"^([a-fA-F0-9]{2}[:-]){5}([a-fA-F0-9]{2})$", ErrorMessage = "MAC address must use : or -  and be 12 numbers or letters A - F to match the IEEE 802 format")]
        public string MacAddress { get; set; }
        public bool Update { get; set; }
        public List<DeviceUpdateAction> UpdateActions { get; set; }


        public Device()
        {
            Id = Guid.NewGuid();
            PartitionKey = this.Id.ToString();
            PreferredHostname = string.Empty;
            Make = string.Empty;
            Model = string.Empty;
            SerialNumber = string.Empty;
            MacAddress = string.Empty;
            Tags = new List<string>();
            UpdateActions = new List<DeviceUpdateAction>();
            Type = typeof(Device).Name;
            ModifiedUTC = DateTime.UtcNow;
            MarkedToDeleteUTC = null;

            CorporateIdentity = string.Empty;
            LastCorpIdentitySync = DateTime.MinValue;
            CorporateIdentityID = string.Empty;

            Status = DeviceStatus.Added;

        }


    }

}
