using System.ComponentModel.DataAnnotations;
using DelegationStationShared.Enums;
using Newtonsoft.Json;

namespace DelegationStationShared.Models
{
    public class DeviceBulk
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "Make is a required field.")]
        [RegularExpression(@"^[a-zA-Z0-9\-_.,&\(\)\s]+$", ErrorMessage = "For make, only use letters, numbers, or the following special characters: -_&().,")]
        public string Make { get; set; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "Model is a required field.")]
        [RegularExpression(@"^[a-zA-Z0-9\-_.,&\(\)+\s]+$", ErrorMessage = "For model, only use letters, numbers, or the following special characters: -_&().+,")]
        public string Model { get; set; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "Serial Number is a required field")]
        [RegularExpression(@"^[a-zA-Z0-9\-_.\s]+$", ErrorMessage = "Only use letters, numbers, -, _, or . for SerialNumber value.")]
        public string SerialNumber { get; set; }

        [RegularExpression(@"^[0-9a-zA-Z](?:[0-9a-zA-Z-]*[0-9a-zA-Z])?$", ErrorMessage = "Only use letters, numbers, or hyphen for Preferred Hostname value. Hyphens must not be first or last character.")]
        [StringLength(15, ErrorMessage = "Preferred Hostname must be 1-15 characters.")]
        [Required(AllowEmptyStrings = false, ErrorMessage ="Preferred Hostname is a required field for new device entries.")]
        public string PreferredHostname { get; set; }

        [Required]
        public DeviceOS? OS { get; set;  }

        public DeviceBulkAction Action { get; set; }

        public DeviceBulk()
        {
            Make = string.Empty;
            Model = string.Empty;
            SerialNumber = string.Empty;
            PreferredHostname = string.Empty;
            OS = DeviceOS.Unknown;
            Action = DeviceBulkAction.add;
        }
    }

    public enum DeviceBulkAction
    {
        add,
        remove
    }
}
