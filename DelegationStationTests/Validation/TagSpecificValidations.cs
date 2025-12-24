using DelegationStation.Validation;
using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using System.ComponentModel.DataAnnotations;

namespace DelegationStationTests.Validation
{
    [TestClass]
    public class NewDeviceValidationTests
    {
        private List<DeviceTag> CreateTestTags()
        {
            return new List<DeviceTag>
            {
                new DeviceTag
                {
                    Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                    Name = "BasicTagNoRename",
                    DeviceRenameEnabled = false
                },
                new DeviceTag
                {
                    Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                    Name = "TagWithRename",
                    DeviceRenameEnabled = true,
                    DeviceNameRegex = @"^[a-zA-Z0-9\-]+$"
                },
                new DeviceTag
                {
                    Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
                    Name = "TagWithRenameNoRegex",
                    DeviceRenameEnabled = true,
                    DeviceNameRegex = ""
                }
            };
        }

        #region Device Validation Tests

        [TestMethod]
        public void ValidateDevice_NoTags_ReturnsError()
        {
            // Arrange
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "hostname",
                OS = DeviceOS.Windows,
                Tags = new List<string>()
            };
            var tags = CreateTestTags();

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, tags);

            // Assert
            Assert.IsTrue(errors.ContainsKey(nameof(device.Tags)));
            Assert.IsTrue(errors[nameof(device.Tags)].Any(e => e.Contains("at least one Tag")));
        }

        [TestMethod]
        public void ValidateDevice_NullTags_ReturnsError()
        {
            // Arrange
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "hostname",
                OS = DeviceOS.Windows,
                Tags = null
            };
            var tags = CreateTestTags();

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, tags);

            // Assert
            Assert.IsTrue(errors.ContainsKey(nameof(device.Tags)));
            Assert.IsTrue(errors[nameof(device.Tags)].Any(e => e.Contains("at least one Tag")));
        }

        [TestMethod]
        public void ValidateDevice_MultipleTags_ReturnsError()
        {
            // Arrange
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "hostname",
                OS = DeviceOS.Windows,
                Tags = new List<string> { "00000000-0000-0000-0000-000000000001", "00000000-0000-0000-0000-000000000002" }
            };
            var tags = CreateTestTags();

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, tags);

            // Assert
            Assert.IsTrue(errors.ContainsKey(nameof(device.Tags)));
            Assert.IsTrue(errors[nameof(device.Tags)].Any(e => e.Contains("only have one Tag")));
        }

        [TestMethod]
        public void ValidateDevice_ValidDeviceWithBasicTagNoRename_ReturnsNoErrors()
        {
            //
            // Arrange
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "hostname",
                OS = DeviceOS.Windows,
                Tags = new List<string> { "00000000-0000-0000-0000-000000000001" }
            };
            var tags = CreateTestTags();

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, tags);

            // Assert
            Assert.AreEqual(0, errors.Count);
        }


        [TestMethod]
        public void ValidateDevice_TagWithRenameEnabled_MissingHostname_ReturnsError()
        {
            // Arrange
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "",
                OS = DeviceOS.Windows,
                Tags = new List<string> { "00000000-0000-0000-0000-000000000002" }
            };
            var tags = CreateTestTags();

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, tags);

            // Assert
            Assert.IsTrue(errors.ContainsKey(nameof(device.PreferredHostname)));
            Assert.IsTrue(errors[nameof(device.PreferredHostname)].Any(e => e.Contains("required for this tag")));
        }

        [TestMethod]
        public void ValidateDevice_TagWithRenameEnabled_NullHostname_ReturnsError()
        {
            // Arrange
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = null,
                OS = DeviceOS.Windows,
                Tags = new List<string> { "00000000-0000-0000-0000-000000000002" }
            };
            var tags = CreateTestTags();

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, tags);

            // Assert
            Assert.IsTrue(errors.ContainsKey(nameof(device.PreferredHostname)));
            Assert.IsTrue(errors[nameof(device.PreferredHostname)].Any(e => e.Contains("required for this tag")));
        }

        [TestMethod]
        public void ValidateDevice_TagWithRenameEnabled_WhitespaceHostname_ReturnsError()
        {
            // Arrange
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "   ",
                OS = DeviceOS.Windows,
                Tags = new List<string> { "00000000-0000-0000-0000-000000000002" }
            };
            var tags = CreateTestTags();

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, tags);

            // Assert
            Assert.IsTrue(errors.ContainsKey(nameof(device.PreferredHostname)));
            Assert.IsTrue(errors[nameof(device.PreferredHostname)].Any(e => e.Contains("required for this tag")));
        }

        [TestMethod]
        public void ValidateDevice_TagWithRenameAndRegex_ValidHostname_ReturnsNoErrors()
        {
            // Arrange
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "valid-hostname-123",
                OS = DeviceOS.Windows,
                Tags = new List<string> { "00000000-0000-0000-0000-000000000002" }
            };
            var tags = CreateTestTags();

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, tags);

            // Assert
            Assert.AreEqual(0, errors.Count);
        }


        [TestMethod]
        public void ValidateDevice_TagWithRenameAndRegex_InvalidHostname_ReturnsError()
        {
            // Arrange
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "invalid_hostname!",
                OS = DeviceOS.Windows,
                Tags = new List<string> { "00000000-0000-0000-0000-000000000002" }
            };
            var tags = CreateTestTags();

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, tags);

            // Assert
            Assert.IsTrue(errors.ContainsKey(nameof(device.PreferredHostname)));
            Assert.IsTrue(errors[nameof(device.PreferredHostname)].Any(e => e.Contains("Does not match regex pattern")));
        }

        [TestMethod]
        public void ValidateDevice_TagWithRenameNoRegex_AnyHostname_ReturnsNoErrors()
        {
            // Arrange
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "any-hostname-works",
                OS = DeviceOS.Windows,
                Tags = new List<string> { "00000000-0000-0000-0000-000000000003" }
            };
            var tags = CreateTestTags();

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, tags);

            // Assert
            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        public void ValidateDevice_TagWithRenameNoRegex_EmptyHostname_ReturnsError()
        {
            // Arrange
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "",
                OS = DeviceOS.Windows,
                Tags = new List<string> { "00000000-0000-0000-0000-000000000003" }
            };
            var tags = CreateTestTags();

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, tags);

            // Assert
            Assert.IsTrue(errors.ContainsKey(nameof(device.PreferredHostname)));
            Assert.IsTrue(errors[nameof(device.PreferredHostname)].Any(e => e.Contains("required for this tag")));
        }



        [TestMethod]
        public void ValidateDevice_EmptyTagList_TagNotFound_ReturnsNoAdditionalErrors()
        {
            // Arrange
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "hostname",
                OS = DeviceOS.Windows,
                Tags = new List<string> { "00000000-0000-0000-0000-000000999999" }
            };
            var tags = CreateTestTags();

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, tags);

            // Assert
            // Should not crash and should not have PreferredHostname errors
            Assert.IsFalse(errors.ContainsKey(nameof(device.PreferredHostname)));
        }

        #endregion

        #region DeviceBulk Validation Tests

        [TestMethod]
        public void ValidateBulkDevice_BasicTagNoRename_ReturnsNoErrors()
        {
            // Arrange
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "hostname",
                OS = DeviceOS.Windows,
                Action = DeviceBulkAction.add
            };
            var tags = CreateTestTags();
            var selectedTags = new List<DeviceTag> { tags[0] };

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);

            // Assert
            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        public void ValidateBulkDevice_TagWithRenameEnabled_MissingHostname_ReturnsError()
        {
            // Arrange
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "",
                OS = DeviceOS.Windows,
                Action = DeviceBulkAction.add
            };
            var tags = CreateTestTags();
            var selectedTags = new List<DeviceTag> { tags[1] }; // Tag with rename enabled

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);

            // Assert
            Assert.IsTrue(errors.ContainsKey("PreferredHostname"));
            Assert.IsTrue(errors["PreferredHostname"].Any(e => e.Contains("required for this tag")));
        }

        [TestMethod]
        public void ValidateBulkDevice_TagWithRenameEnabled_NullHostname_ReturnsError()
        {
            // Arrange
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = null,
                OS = DeviceOS.Windows,
                Action = DeviceBulkAction.add
            };
            var tags = CreateTestTags();
            var selectedTags = new List<DeviceTag> { tags[1] }; // Tag with rename enabled

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);

            // Assert
            Assert.IsTrue(errors.ContainsKey("PreferredHostname"));
            Assert.IsTrue(errors["PreferredHostname"].Any(e => e.Contains("required for this tag")));
        }

        [TestMethod]
        public void ValidateBulkDevice_TagWithRenameEnabled_WhitespaceHostname_ReturnsError()
        {
            // Arrange
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "   ",
                OS = DeviceOS.Windows,
                Action = DeviceBulkAction.add
            };
            var tags = CreateTestTags();
            var selectedTags = new List<DeviceTag> { tags[1] }; // Tag with rename enabled

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);

            // Assert
            Assert.IsTrue(errors.ContainsKey("PreferredHostname"));
            Assert.IsTrue(errors["PreferredHostname"].Any(e => e.Contains("required for this tag")));
        }

        [TestMethod]
        public void ValidateBulkDevice_TagWithRenameAndRegex_ValidHostname_ReturnsNoErrors()
        {
            // Arrange
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "valid-hostname-123",
                OS = DeviceOS.Windows,
                Action = DeviceBulkAction.add
            };
            var tags = CreateTestTags();
            var selectedTags = new List<DeviceTag> { tags[1] }; // Tag with rename and regex

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);

            // Assert
            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        public void ValidateBulkDevice_TagWithRenameAndRegex_InvalidHostname_ReturnsError()
        {
            // Arrange
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "invalid_hostname!",
                OS = DeviceOS.Windows,
                Action = DeviceBulkAction.add
            };
            var tags = CreateTestTags();
            var selectedTags = new List<DeviceTag> { tags[1] }; // Tag with rename and regex

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);

            // Assert
            Assert.IsTrue(errors.ContainsKey("PreferredHostname"));
            Assert.IsTrue(errors["PreferredHostname"].Any(e => e.Contains("Does not match regex pattern")));
        }

        [TestMethod]
        public void ValidateBulkDevice_TagWithRenameNoRegex_AnyHostname_ReturnsNoErrors()
        {
            // Arrange
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "any-hostname-works",
                OS = DeviceOS.Windows,
                Action = DeviceBulkAction.add
            };
            var tags = CreateTestTags();
            var selectedTags = new List<DeviceTag> { tags[2] }; // Tag with rename but no regex

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);

            // Assert
            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        public void ValidateBulkDevice_TagWithRenameNoRegex_EmptyHostname_ReturnsError()
        {
            // Arrange
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "",
                OS = DeviceOS.Windows,
                Action = DeviceBulkAction.add
            };
            var tags = CreateTestTags();
            var selectedTags = new List<DeviceTag> { tags[2] }; // Tag with rename but no regex

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);

            // Assert
            Assert.IsTrue(errors.ContainsKey("PreferredHostname"));
            Assert.IsTrue(errors["PreferredHostname"].Any(e => e.Contains("required for this tag")));
        }

        [TestMethod]
        public void ValidateBulkDevice_NoTags_ReturnsError()
        {
            // Arrange
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "hostname",
                OS = DeviceOS.Windows,
                Action = DeviceBulkAction.add
            };
            var selectedTags = new List<DeviceTag>();

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);

            // Assert
            Assert.IsTrue(errors.ContainsKey("Tags"));
            Assert.IsTrue(errors["Tags"].Any(e => e.Contains("at least one Tag")));
        }

        [TestMethod]
        public void ValidateBulkDevice_MultipleTags_ReturnsError()
        {
            // Arrange
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "hostname",
                OS = DeviceOS.Windows,
                Action = DeviceBulkAction.add
            };
            var tags = CreateTestTags();
            var selectedTags = new List<DeviceTag> { tags[0], tags[1] };

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);

            // Assert
            Assert.IsTrue(errors.ContainsKey("Tags"));
            Assert.IsTrue(errors["Tags"].Any(e => e.Contains("only have one Tag")));
        }

        //TODO:  This fails, but I think this is okay b/c I don't think we do validation in this case?
        //confirm removal works
        [TestMethod]
        public void ValidateBulkDevice_RemoveAction_NoHostnameRequired_ReturnsNoErrors()
        {
            // Arrange
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "",
                OS = DeviceOS.Windows,
                Action = DeviceBulkAction.remove
            };
            var tags = CreateTestTags();
            var selectedTags = new List<DeviceTag> { tags[1] }; // Tag with rename enabled

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);

            // Assert
            // For remove action, hostname validation should be skipped
            Assert.AreEqual(0, errors.Count);
        }

        //TODO:  Is this what we want?
        [TestMethod]
        public void ValidateBulkDevice_EmptyTagList_TagNotFound_ReturnsNoAdditionalErrors()
        {
            // Arrange
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "hostname",
                OS = DeviceOS.Windows,
                Action = DeviceBulkAction.add
            };
            // Creating a tag that won't be in the selected list
            var selectedTags = new List<DeviceTag>
            {
                new DeviceTag
                {
                    Id = Guid.Parse("00000000-0000-0000-0000-000000999999"),
                    Name = "NonExistentTag",
                    DeviceRenameEnabled = false
                }
            };

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);

            // Assert
            // Should not crash
            Assert.IsFalse(errors.ContainsKey("PreferredHostname"));
        }

        #endregion

    }
}