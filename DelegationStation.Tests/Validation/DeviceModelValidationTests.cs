using System.ComponentModel.DataAnnotations;
using DelegationStationShared.Enums;

namespace DelegationStation.Tests.Validation
{
    [TestClass]
    public class DeviceModelValidationTests
    {
        [TestMethod]
        [DynamicData(nameof(ValidationCases))]
        public void SharedFieldsFollowTheSameValidationContract(
            string modelKind, string propertyName, object? value, bool expectedValid)
        {
            object device = modelKind == nameof(Device)
                ? new Device
                {
                    Make = "Make",
                    Model = "Model",
                    SerialNumber = "12345",
                    PreferredHostname = "hostname",
                    OS = DeviceOS.Windows
                }
                : new DeviceBulk
                {
                    Make = "Make",
                    Model = "Model",
                    SerialNumber = "12345",
                    PreferredHostname = "hostname",
                    OS = DeviceOS.Windows,
                    Action = Enum.Parse<DeviceBulkAction>(modelKind)
                };

            var property = device.GetType().GetProperty(propertyName);
            Assert.IsNotNull(property);
            property.SetValue(device, value);

            var results = new List<ValidationResult>();
            bool actualValid = Validator.TryValidateObject(
                device, new ValidationContext(device), results, validateAllProperties: true);

            Assert.AreEqual(expectedValid, actualValid,
                $"{modelKind}.{propertyName}: {string.Join("; ", results.Select(r => r.ErrorMessage))}");
            if (!expectedValid)
            {
                Assert.IsTrue(results.Any(r => r.MemberNames.Contains(propertyName)),
                    $"Expected a validation error for {modelKind}.{propertyName}.");
            }
        }

        public static IEnumerable<object?[]> ValidationCases()
        {
            foreach (string modelKind in new[] { nameof(Device), nameof(DeviceBulkAction.add), nameof(DeviceBulkAction.remove) })
            {
                foreach (var (property, value, valid) in SharedFieldCases())
                {
                    yield return new object?[] { modelKind, property, value, valid };
                }
            }
        }

        private static IEnumerable<(string Property, object? Value, bool Valid)> SharedFieldCases()
        {
            foreach (string property in new[] { nameof(Device.Make), nameof(Device.Model), nameof(Device.SerialNumber) })
            {
                yield return (property, null, false);
                yield return (property, "", false);
                yield return (property, " ", false);
                yield return (property, "   ", false);
                yield return (property, "Valid123", true);
                yield return (property, " Valid 123 ", true);

                string allowedPunctuation = property == nameof(Device.SerialNumber)
                    ? "-_."
                    : property == nameof(Device.Model) ? "-_.,&()+" : "-_.,&()";

                foreach (char punctuation in "-_.,&()+!@#$%^*={}[]\\ /|?<>~'\"`")
                {
                    yield return (property, $"Valid123{punctuation}",
                        punctuation == ' ' || allowedPunctuation.Contains(punctuation));
                }
            }

            yield return (nameof(Device.PreferredHostname), null, true);
            yield return (nameof(Device.PreferredHostname), "", true);
            foreach (string hostname in new[] { "a", "1", "ValidHostname12", "123456789012345", "valid-hostname", "valid-host-name" })
            {
                yield return (nameof(Device.PreferredHostname), hostname, true);
            }
            foreach (string hostname in new[] { " ", "-hostname", "hostname-", "1234567890123456" })
            {
                yield return (nameof(Device.PreferredHostname), hostname, false);
            }
            foreach (char punctuation in "_.,&()+!@#$%^*={}[]\\ /|?<>~'\"`")
            {
                yield return (nameof(Device.PreferredHostname), $"host{punctuation}name", false);
            }

            // Exercise every .NET whitespace character, including Unicode, in each position.
            foreach (char whitespace in Enumerable.Range(char.MinValue, char.MaxValue + 1)
                .Select(value => (char)value).Where(c => char.IsWhiteSpace(c) && c != ' '))
            {
                foreach (string property in new[]
                {
                    nameof(Device.Make), nameof(Device.Model),
                    nameof(Device.SerialNumber), nameof(Device.PreferredHostname)
                })
                {
                    yield return (property, $"{whitespace}Value", false);
                    yield return (property, $"Val{whitespace}ue", false);
                    yield return (property, $"Value{whitespace}", false);
                    yield return (property, whitespace.ToString(), false);
                }
            }

            yield return (nameof(Device.OS), null, false);
            yield return (nameof(Device.OS), DeviceOS.Windows, true);
        }
    }
}
