using DelegationStationShared.Models;
using System.Text.RegularExpressions;


namespace DelegationStation.Validation
{
    public static class NewDeviceValidation
    {

        public static Dictionary<string, List<string>> ValidateBulkDevice(DeviceBulk deviceBulk, List<DeviceTag> selectedTags)
        {
            var device = new Device
            {
                Make = deviceBulk.Make,
                Model = deviceBulk.Model,
                SerialNumber = deviceBulk.SerialNumber,
                PreferredHostname = deviceBulk.PreferredHostname,
                OS = deviceBulk.OS,
                Tags = selectedTags.Select(t => t.Id.ToString()).ToList()
            };
            return ValidateDevice(device, selectedTags);
        }

        public static Dictionary<string, List<string>> ValidateDevice(Device device, List<DeviceTag> selectedTags)
        {
            var errors = new Dictionary<string, List<string>>();

            //
            //  Field Validations handled by DataAnnotations in Model
            //


            // Validate Tag is selected
            if (device.Tags == null || device.Tags.Count == 0)
            {
                AddError(errors, nameof(device.Tags), "Device must have at least one Tag.");
            }
            else if (device.Tags.Count > 1)
            {
                AddError(errors, nameof(device.Tags), "Device must only have one Tag.");
            }


            //
            // Validate Preferred Hostname against settings for selected tag
            //
            DeviceTag? tag = null;
            if (device.Tags != null && device.Tags.Count == 1)
            {
                tag = selectedTags.FirstOrDefault(t => t.Id.ToString() == device.Tags[0]);
            }

            //
            // For tags where DeviceRenameEnabled is true, validate PreferredHostname
            // against tag specific regex
            //
            if (tag != null && tag.DeviceRenameEnabled)
            {
                if (string.IsNullOrWhiteSpace(device.PreferredHostname))
                {
                    AddError(errors, nameof(device.PreferredHostname), "Preferred Hostname is required for this tag.");
                }
                else
                {
                    // If Regex isn't set, treat as valid and skip regex
                    if (!string.IsNullOrWhiteSpace(tag.DeviceNameRegex))
                    {
                        // add step to validate regex
                        if (!Regex.IsMatch(device.PreferredHostname, tag.DeviceNameRegex))
                        {
                            AddError(errors, nameof(device.PreferredHostname), $"Does not match regex pattern required for this tag: {tag.DeviceNameRegex}");
                        }
                    }
                }
            }


            return errors;
        }

        private static void AddError(Dictionary<string, List<string>> errors, string fieldName, string errorMessage)
        {
            if (!errors.ContainsKey(fieldName))
            {
                errors[fieldName] = new List<string>();
            }
            errors[fieldName].Add(errorMessage);
        }
    }
}
