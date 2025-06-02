using System.ComponentModel.DataAnnotations;
using DelegationStationShared.Enums;
using Newtonsoft.Json;

namespace DelegationStationShared.Models
{
    public class DeviceBulk
    {
        [Required(AllowEmptyStrings = false)]
        [RegularExpression(@"^[a-zA-Z0-9\-_.,&\(\)\s]+$", ErrorMessage = "For make, only use letters, numbers, or the following special characters: -_&().,")]
        public string Make { get; set; }

        [Required(AllowEmptyStrings = false)]

        [RegularExpression(@"^[a-zA-Z0-9\-_.,&\(\)+\s]+$", ErrorMessage = "For model, only use letters, numbers, or the following special characters: -_&().+,")]
        public string Model { get; set; }


        [Required(AllowEmptyStrings = false)]
        [RegularExpression(@"^[a-zA-Z0-9\-_.\s]+$", ErrorMessage = "Only use letters, numbers, -, _, or . for SerialNumber value.")]
        public string SerialNumber { get; set; }

        [RegularExpression(@"^[a-zA-z0-9\-]*", ErrorMessage = "Only use letters, numbers, or hyphen for Preferred Host Name value.")]
        public string PreferredHostName { get; set; }

        public DeviceOS? OS { get; set;  }

        public DeviceBulkAction Action { get; set; }

        public DeviceBulk()
        {
            Make = string.Empty;
            Model = string.Empty;
            SerialNumber = string.Empty;
            PreferredHostName = string.Empty;
            Action = DeviceBulkAction.add;
        }
    }

    public enum DeviceBulkAction
    {
        add,
        remove
    }
}
