using DelegationStation.Validation;
using DelegationStationShared.Enums;

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
                    Name = "NoRenameNoRegex",
                    DeviceRenameEnabled = false
                },
                new DeviceTag
                {
                    Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                    Name = "RenameNoRegex",
                    DeviceRenameEnabled = true,
                },
                new DeviceTag
                {
                    Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
                    Name = "RegexNoRename",
                    DeviceRenameEnabled = true,
                    DeviceNameRegex = @"^match$"
                },
                new DeviceTag
                {
                    Id = Guid.Parse("00000000-0000-0000-0000-000000000004"),
                    Name = "RenameAndRegex",
                    DeviceRenameEnabled = true,
                    DeviceNameRegex = @"^match$"
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
        [DataRow("")]
        [DataRow(null)]
        [DataRow("validhost")]
        public void ValidateDevice_NoRenameNoRegex_ValidandEmptyHostnames_Allowed(string hostname)
        {
            //
            // Arrange
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
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
        [DataRow("")]
        [DataRow(null)]
        public void ValidateDevice_RenameNoRegex_MissingHostname_ReturnsError(string hostname)
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
        public void ValidateDevice_RenameNoRegex_AnyHostname_ReturnsNoErrors()
        {
            // Arrange
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "any-hostname-works",
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
        [DataRow("")]
        [DataRow(null)]
        [DataRow("notamatch")]
        public void ValidateDevice_RegexNoRename_NoMatch_Errors(string hostname)
        {
            // Arrange
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows,
                Tags = new List<string> { "00000000-0000-0000-0000-000000000003" }
            };
            var tags = CreateTestTags();

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, tags);

            // Assert
            Assert.IsTrue(errors.ContainsKey(nameof(device.PreferredHostname)));
            Assert.IsTrue(errors[nameof(device.PreferredHostname)].Any(e => e.Contains("Does not match regex")));
        }


        [TestMethod]
        [DataRow("match")]
        public void ValidateDevice_RegexNoRename_Matches_Allowed(string hostname)
        {
            // Arrange
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
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
        [DataRow("")]
        [DataRow(null)]
        public void ValidateDevice_RegexAndRename_MissingHostname_Errors(string hostname)
        {
            // Arrange
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows,
                Tags = new List<string> { "00000000-0000-0000-0000-000000000004" }
            };
            var tags = CreateTestTags();

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, tags);

            // Assert
            Assert.IsTrue(errors.ContainsKey(nameof(device.PreferredHostname)));
            Assert.IsTrue(errors[nameof(device.PreferredHostname)].Any(e => e.Contains("required for this tag")));
        }

        [TestMethod]
        [DataRow("nomatch")]
        public void ValidateDevice_RegexAndRename_NoMatch_Errors(string hostname)
        {
            // Arrange
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows,
                Tags = new List<string> { "00000000-0000-0000-0000-000000000004" }
            };
            var tags = CreateTestTags();

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, tags);

            // Assert
            Assert.IsTrue(errors.ContainsKey(nameof(device.PreferredHostname)));
            Assert.IsTrue(errors[nameof(device.PreferredHostname)].Any(e => e.Contains("Does not match regex")));
        }

        [TestMethod]
        [DataRow("match")]
        public void ValidateDevice_RegexAndRename_Matches_Allowed(string hostname)
        {
            // Arrange
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows,
                Tags = new List<string> { "00000000-0000-0000-0000-000000000004" }
            };
            var tags = CreateTestTags();

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, tags);

            // Assert
            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        public void ValidateDevice_InvalidRegexPattern_Errors()
        {
            // Arrange
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "hostname",
                OS = DeviceOS.Windows,
                Tags = new List<string> { "00000000-0000-0000-0000-000000000005" }
            };
            var tags = new List<DeviceTag>
    {
        new DeviceTag
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000005"),
            Name = "InvalidRegex",
            DeviceRenameEnabled = true,
            DeviceNameRegex = @"[invalid(regex" // Invalid regex
        }
    };

            // Act & Assert
            // Should either handle gracefully or throw expected exception
            var errors = NewDeviceValidation.ValidateDevice(device, tags);

            // Assert
            Assert.IsTrue(errors.ContainsKey(nameof(device.PreferredHostname)));
            Assert.IsTrue(errors[nameof(device.PreferredHostname)].Any(e => e.Contains("Cannot validate")));
        }

        #endregion

        #region DeviceBulk Validation Tests

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
                OS = DeviceOS.Windows
            };
            var tags = new List<DeviceTag>();

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, tags);

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
                OS = DeviceOS.Windows
            };
            var tags = CreateTestTags();
            var selectedTags = new List<DeviceTag>
            {
                tags[0],
                tags[1]
            };

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);

            // Assert
            Assert.IsTrue(errors.ContainsKey("Tags"));
            Assert.IsTrue(errors["Tags"].Any(e => e.Contains("only have one Tag")));
        }

        [TestMethod]
        [DataRow("")]
        [DataRow(null)]
        [DataRow("validhost")]
        public void ValidateBulkDevice_NoRenameNoRegex_ValidandEmptyHostnames_Allowed(string hostname)
        {
            // Arrange
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows
            };
            var tags = CreateTestTags();
            var selectedTags = new List<DeviceTag> { tags[0] };

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);

            // Assert
            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow(null)]
        public void ValidateBulkDevice_RenameNoRegex_MissingHostname_ReturnsError(string hostname)
        {
            // Arrange
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows
            };
            var tags = CreateTestTags();
            var selectedTags = new List<DeviceTag> { tags[1] };

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);

            // Assert
            Assert.IsTrue(errors.ContainsKey("PreferredHostname"));
            Assert.IsTrue(errors["PreferredHostname"].Any(e => e.Contains("required for this tag")));
        }

        [TestMethod]
        public void ValidateBulkDevice_RenameNoRegex_AnyHostname_ReturnsNoErrors()
        {
            // Arrange
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "any-hostname-works",
                OS = DeviceOS.Windows
            };
            var tags = CreateTestTags();
            var selectedTags = new List<DeviceTag> { tags[1] };

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);

            // Assert
            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow(null)]
        [DataRow("notamatch")]
        public void ValidateBulkDevice_RegexNoRename_NoMatch_Errors(string hostname)
        {
            // Arrange
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows
            };
            var tags = CreateTestTags();
            var selectedTags = new List<DeviceTag> { tags[2] };

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);

            // Assert
            Assert.IsTrue(errors.ContainsKey("PreferredHostname"));
            Assert.IsTrue(errors["PreferredHostname"].Any(e => e.Contains("Does not match regex")));
        }

        [TestMethod]
        [DataRow("match")]
        public void ValidateBulkDevice_RegexNoRename_Matches_Allowed(string hostname)
        {
            // Arrange
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows
            };
            var tags = CreateTestTags();
            var selectedTags = new List<DeviceTag> { tags[2] };

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);

            // Assert
            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow(null)]
        public void ValidateBulkDevice_RegexAndRename_MissingHostname_Errors(string hostname)
        {
            // Arrange
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows
            };
            var tags = CreateTestTags();
            var selectedTags = new List<DeviceTag> { tags[3] };

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);

            // Assert
            Assert.IsTrue(errors.ContainsKey("PreferredHostname"));
            Assert.IsTrue(errors["PreferredHostname"].Any(e => e.Contains("required for this tag")));
        }

        [TestMethod]
        [DataRow("nomatch")]
        public void ValidateBulkDevice_RegexAndRename_NoMatch_Errors(string hostname)
        {
            // Arrange
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows
            };
            var tags = CreateTestTags();
            var selectedTags = new List<DeviceTag> { tags[3] };

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);

            // Assert
            Assert.IsTrue(errors.ContainsKey("PreferredHostname"));
            Assert.IsTrue(errors["PreferredHostname"].Any(e => e.Contains("Does not match regex")));
        }

        [TestMethod]
        [DataRow("match")]
        public void ValidateBulkDevice_RegexAndRename_Matches_Allowed(string hostname)
        {
            // Arrange
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows
            };
            var tags = CreateTestTags();
            var selectedTags = new List<DeviceTag> { tags[3] };

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);

            // Assert
            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        public void ValidateBulkDevice_InvalidRegexPattern_HandlesGracefully()
        {
            // Arrange
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = "hostname",
                OS = DeviceOS.Windows,
            };
            var selectedTags = new List<DeviceTag>
            {
                new DeviceTag
                {
                    Id = Guid.Parse("00000000-0000-0000-0000-000000000005"),
                    Name = "InvalidRegex",
                    DeviceRenameEnabled = true,
                    DeviceNameRegex = @"[invalid(regex" // Invalid regex
                }
            };

            // Act & Assert
            // Should either handle gracefully or throw expected exception
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, selectedTags);


            // Assert
            Assert.IsTrue(errors.ContainsKey(nameof(deviceBulk.PreferredHostname)));
            Assert.IsTrue(errors[nameof(deviceBulk.PreferredHostname)].Any(e => e.Contains("Cannot validate")));
        }

        #endregion

    }
}