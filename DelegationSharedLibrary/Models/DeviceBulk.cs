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

        // Validation applicable to all devices done here
        // Validation for specific tags handled in custom validation class
        [RegularExpression(@"^$|^[0-9a-zA-Z](?:[0-9a-zA-Z-]*[0-9a-zA-Z])?$", ErrorMessage = "Only use letters, numbers, or hyphen for Preferred Hostname value. Hyphens may not be at beginning or end.")]
        [MaxLength(15, ErrorMessage = "Preferred Hostname cannot exceed 15 characters.")]
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
