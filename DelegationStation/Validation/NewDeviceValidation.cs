using DelegationStationShared.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;


namespace DelegationStation.Validation
{
    public static class NewDeviceValidation
    {

        public static Dictionary<string, List<string>> ValidateBulkDevice(DeviceBulk deviceBulk, List<DeviceTag> selectedTags, ILogger? logger = null)
        {
            logger?.LogInformation("Starting bulk device validation for SerialNumber: {SerialNumber}", deviceBulk.SerialNumber);
            
            var device = new Device
            {
                Make = deviceBulk.Make,
                Model = deviceBulk.Model,
                SerialNumber = deviceBulk.SerialNumber,
                PreferredHostname = deviceBulk.PreferredHostname,
                OS = deviceBulk.OS,
                Tags = selectedTags.Select(t => t.Id.ToString()).ToList()
            };
            return ValidateDevice(device, selectedTags, logger);
        }

        public static Dictionary<string, List<string>> ValidateDevice(Device device, List<DeviceTag> selectedTags, ILogger? logger = null)
        {
            logger?.LogInformation("Starting device validation for SerialNumber: {SerialNumber}, Tags: {TagCount}", 
                device.SerialNumber, device.Tags?.Count ?? 0);
            
            var errors = new Dictionary<string, List<string>>();

            //
            //  Field Validations handled by DataAnnotations in Model
            //


            // Validate Tag is selected
            if (device.Tags == null || device.Tags.Count == 0)
            {
                logger?.LogWarning("Device validation failed: No tags selected for device {SerialNumber}", device.SerialNumber);
                AddError(errors, nameof(device.Tags), "Device must have at least one Tag.");
            }
            else if (device.Tags.Count > 1)
            {
                logger?.LogWarning("Device validation failed: Multiple tags selected for device {SerialNumber}. Count: {TagCount}", 
                    device.SerialNumber, device.Tags.Count);
                AddError(errors, nameof(device.Tags), "Device must only have one Tag.");
            }


            //
            // Validate Preferred Hostname against settings for selected tag
            //
            DeviceTag? tag = null;
            if (device.Tags != null && device.Tags.Count == 1)
            {
                tag = selectedTags.FirstOrDefault(t => t.Id.ToString() == device.Tags[0]);
                if (tag == null)
                {
                    logger?.LogWarning("Device validation: Tag {TagId} not found in selected tags for device {SerialNumber}", 
                        device.Tags[0], device.SerialNumber);
                }
                else
                {
                    logger?.LogDebug("Validating device {SerialNumber} against tag {TagId} ({TagName})", 
                        device.SerialNumber, tag.Id, tag.Name);
                }
            }

            //
            // Tag-specific validations require tag to not be null
            //
            if (tag != null)
            {
                //
                // If DeviceRenameEnabled is set, verify hostname is provided
                //
                if (tag.DeviceRenameEnabled)
                {
                    logger?.LogDebug("Tag {TagId} has DeviceRenameEnabled. Validating hostname for device {SerialNumber}", 
                        tag.Id, device.SerialNumber);
                    
                    if (string.IsNullOrWhiteSpace(device.PreferredHostname))
                    {
                        logger?.LogWarning("Device validation failed: Preferred hostname required but not provided for device {SerialNumber}, Tag: {TagId}", 
                            device.SerialNumber, tag.Id);
                        AddError(errors, nameof(device.PreferredHostname), "Preferred Hostname is required for this tag.");
                    }
                }

                //
                // if deviceRegex is set for tag, validate PreferredHostname against it
                // regardless of whether DeviceRenameEnabled is set
                //
                // This ensures device name meets the standard when the tag has renaming enabled
                //

                // If Regex isn't set, treat as valid and skip regex
                if (!string.IsNullOrWhiteSpace(tag.DeviceNameRegex))
                {
                    logger?.LogDebug("Validating hostname against regex for device {SerialNumber}, Tag: {TagId}, Regex: {Regex}", 
                        device.SerialNumber, tag.Id, tag.DeviceNameRegex);

                    // Ensure regex is valid
                    if(!IsRegexPatternValid(tag.DeviceNameRegex))
                    {
                        logger?.LogError("Invalid regex pattern configured for tag {TagId}: {Regex}", 
                            tag.Id, tag.DeviceNameRegex);
                        AddError(errors, nameof(device.PreferredHostname), "Cannot validate field.  Contact administrator.");
                    }

                    // add step to validate regex
                    else if (!Regex.IsMatch(device.PreferredHostname ?? "", tag.DeviceNameRegex))
                    {
                        logger?.LogWarning("Device validation failed: Hostname {Hostname} does not match regex for device {SerialNumber}, Tag: {TagId}", 
                            device.PreferredHostname, device.SerialNumber, tag.Id);
                        AddError(errors, nameof(device.PreferredHostname), $"Does not match name requirements for this tag: {tag.DeviceNameRegexDescription}");
                    }
                }
            }

            if (errors.Count > 0)
            {
                logger?.LogWarning("Device validation completed with {ErrorCount} error(s) for device {SerialNumber}", 
                    errors.Count, device.SerialNumber);
            }
            else
            {
                logger?.LogInformation("Device validation completed successfully for device {SerialNumber}", device.SerialNumber);
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

        private static bool IsRegexPatternValid(string pattern)
        {
            try
            {
                new System.Text.RegularExpressions.Regex(pattern);
            }
            catch (ArgumentException)
            {
                return false;
            }
            return true;
        }
    }
}
