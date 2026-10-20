using DelegationStation.Validation;
using DelegationStationShared.Enums;

namespace DelegationStation.Tests.Validation
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
                    DeviceRenameEnabled = false,
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
            Assert.IsTrue(errors[nameof(device.PreferredHostname)].Any(e => e.Contains("Does not match name requirements")));
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
            Assert.IsTrue(errors[nameof(device.PreferredHostname)].Any(e => e.Contains("Does not match name requirements")));
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
            Assert.IsTrue(errors["PreferredHostname"].Any(e => e.Contains("Does not match name requirements")));
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
            Assert.IsTrue(errors["PreferredHostname"].Any(e => e.Contains("Does not match name requirements")));
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

        #region Admin and Non-Admin Device Name Validation Tests

        // Admin users receive tags via SELECT * (all fields populated).
        // Non-admin users receive tags via an explicit field list that omits
        // DeviceRenameEnabled, so it defaults to false even when the tag has
        // renaming configured. DeviceNameRegex IS included in the non-admin query.
        // Device name validation must enforce regex for both user types.

        [TestMethod]
        [DataRow("ABC-1234")]
        [DataRow("XYZ-0001")]
        public void ValidateDevice_AdminUser_RegexTag_MatchingHostname_NoErrors(string hostname)
        {
            // Arrange - Admin gets all fields including DeviceRenameEnabled
            var tag = new DeviceTag
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000010"),
                Name = "AdminRegexTag",
                DeviceRenameEnabled = true,
                DeviceNameRegex = @"^[A-Z]{3}-\d{4}$",
                DeviceNameRegexDescription = "Must match format AAA-0000"
            };
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows,
                Tags = new List<string> { tag.Id.ToString() }
            };

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, new List<DeviceTag> { tag });

            // Assert
            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        [DataRow("invalid")]
        [DataRow("abc-1234")]
        [DataRow("ABCD-1234")]
        public void ValidateDevice_AdminUser_RegexTag_NonMatchingHostname_ReturnsError(string hostname)
        {
            // Arrange - Admin gets all fields including DeviceRenameEnabled
            var tag = new DeviceTag
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000010"),
                Name = "AdminRegexTag",
                DeviceRenameEnabled = true,
                DeviceNameRegex = @"^[A-Z]{3}-\d{4}$",
                DeviceNameRegexDescription = "Must match format AAA-0000"
            };
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows,
                Tags = new List<string> { tag.Id.ToString() }
            };

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, new List<DeviceTag> { tag });

            // Assert
            Assert.IsTrue(errors.ContainsKey(nameof(device.PreferredHostname)));
            Assert.IsTrue(errors[nameof(device.PreferredHostname)].Any(e => e.Contains("Does not match name requirements")));
        }

        [TestMethod]
        [DataRow("")]
        [DataRow(null)]
        public void ValidateDevice_AdminUser_RenameAndRegexTag_EmptyHostname_ReturnsRequiredError(string hostname)
        {
            // Arrange - Admin gets DeviceRenameEnabled = true from SELECT *
            var tag = new DeviceTag
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000010"),
                Name = "AdminRenameRegexTag",
                DeviceRenameEnabled = true,
                DeviceNameRegex = @"^[A-Z]{3}-\d{4}$",
                DeviceNameRegexDescription = "Must match format AAA-0000"
            };
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows,
                Tags = new List<string> { tag.Id.ToString() }
            };

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, new List<DeviceTag> { tag });

            // Assert
            Assert.IsTrue(errors.ContainsKey(nameof(device.PreferredHostname)));
            Assert.IsTrue(errors[nameof(device.PreferredHostname)].Any(e => e.Contains("required for this tag")));
        }

        [TestMethod]
        [DataRow("ABC-1234")]
        [DataRow("XYZ-0001")]
        public void ValidateDevice_NonAdminUser_RegexTag_MatchingHostname_NoErrors(string hostname)
        {
            // Arrange - Non-admin query omits DeviceRenameEnabled (defaults false)
            // but includes DeviceNameRegex
            var tag = new DeviceTag
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000011"),
                Name = "NonAdminRegexTag",
                // DeviceRenameEnabled not set (defaults to false, as non-admin query omits it)
                DeviceNameRegex = @"^[A-Z]{3}-\d{4}$",
                DeviceNameRegexDescription = "Must match format AAA-0000"
            };
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows,
                Tags = new List<string> { tag.Id.ToString() }
            };

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, new List<DeviceTag> { tag });

            // Assert
            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        [DataRow("invalid")]
        [DataRow("abc-1234")]
        [DataRow("ABCD-1234")]
        public void ValidateDevice_NonAdminUser_RegexTag_NonMatchingHostname_ReturnsError(string hostname)
        {
            // Arrange - Non-admin query omits DeviceRenameEnabled but includes DeviceNameRegex
            var tag = new DeviceTag
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000011"),
                Name = "NonAdminRegexTag",
                DeviceNameRegex = @"^[A-Z]{3}-\d{4}$",
                DeviceNameRegexDescription = "Must match format AAA-0000"
            };
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows,
                Tags = new List<string> { tag.Id.ToString() }
            };

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, new List<DeviceTag> { tag });

            // Assert
            Assert.IsTrue(errors.ContainsKey(nameof(device.PreferredHostname)));
            Assert.IsTrue(errors[nameof(device.PreferredHostname)].Any(e => e.Contains("Does not match name requirements")));
        }

        [TestMethod]
        [DataRow("")]
        [DataRow(null)]
        public void ValidateDevice_NonAdminUser_RegexTag_EmptyHostname_ReturnsRegexError(string hostname)
        {
            // Arrange - Non-admin: DeviceRenameEnabled defaults false so no "required" error,
            // but regex validation still runs and rejects empty/null hostnames
            var tag = new DeviceTag
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000011"),
                Name = "NonAdminRegexTag",
                DeviceNameRegex = @"^[A-Z]{3}-\d{4}$",
                DeviceNameRegexDescription = "Must match format AAA-0000"
            };
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows,
                Tags = new List<string> { tag.Id.ToString() }
            };

            // Act
            var errors = NewDeviceValidation.ValidateDevice(device, new List<DeviceTag> { tag });

            // Assert
            Assert.IsTrue(errors.ContainsKey(nameof(device.PreferredHostname)));
            Assert.IsTrue(errors[nameof(device.PreferredHostname)].Any(e => e.Contains("Does not match name requirements")));
        }

        [TestMethod]
        [DataRow("ABC-1234")]
        public void ValidateBulkDevice_AdminUser_RegexTag_MatchingHostname_NoErrors(string hostname)
        {
            // Arrange - Admin gets all fields
            var tag = new DeviceTag
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000010"),
                Name = "AdminRegexTag",
                DeviceRenameEnabled = true,
                DeviceNameRegex = @"^[A-Z]{3}-\d{4}$",
                DeviceNameRegexDescription = "Must match format AAA-0000"
            };
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows
            };

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, new List<DeviceTag> { tag });

            // Assert
            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        [DataRow("invalid")]
        [DataRow("abc-1234")]
        public void ValidateBulkDevice_AdminUser_RegexTag_NonMatchingHostname_ReturnsError(string hostname)
        {
            // Arrange - Admin gets all fields
            var tag = new DeviceTag
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000010"),
                Name = "AdminRegexTag",
                DeviceRenameEnabled = true,
                DeviceNameRegex = @"^[A-Z]{3}-\d{4}$",
                DeviceNameRegexDescription = "Must match format AAA-0000"
            };
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows
            };

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, new List<DeviceTag> { tag });

            // Assert
            Assert.IsTrue(errors.ContainsKey("PreferredHostname"));
            Assert.IsTrue(errors["PreferredHostname"].Any(e => e.Contains("Does not match name requirements")));
        }

        [TestMethod]
        [DataRow("")]
        [DataRow(null)]
        public void ValidateBulkDevice_AdminUser_RenameAndRegexTag_EmptyHostname_ReturnsRequiredError(string hostname)
        {
            // Arrange - Admin gets DeviceRenameEnabled = true
            var tag = new DeviceTag
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000010"),
                Name = "AdminRenameRegexTag",
                DeviceRenameEnabled = true,
                DeviceNameRegex = @"^[A-Z]{3}-\d{4}$",
                DeviceNameRegexDescription = "Must match format AAA-0000"
            };
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows
            };

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, new List<DeviceTag> { tag });

            // Assert
            Assert.IsTrue(errors.ContainsKey("PreferredHostname"));
            Assert.IsTrue(errors["PreferredHostname"].Any(e => e.Contains("required for this tag")));
        }

        [TestMethod]
        [DataRow("ABC-1234")]
        public void ValidateBulkDevice_NonAdminUser_RegexTag_MatchingHostname_NoErrors(string hostname)
        {
            // Arrange - Non-admin: DeviceRenameEnabled defaults false, DeviceNameRegex present
            var tag = new DeviceTag
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000011"),
                Name = "NonAdminRegexTag",
                DeviceNameRegex = @"^[A-Z]{3}-\d{4}$",
                DeviceNameRegexDescription = "Must match format AAA-0000"
            };
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows
            };

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, new List<DeviceTag> { tag });

            // Assert
            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        [DataRow("invalid")]
        [DataRow("abc-1234")]
        public void ValidateBulkDevice_NonAdminUser_RegexTag_NonMatchingHostname_ReturnsError(string hostname)
        {
            // Arrange - Non-admin: DeviceRenameEnabled defaults false, DeviceNameRegex present
            var tag = new DeviceTag
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000011"),
                Name = "NonAdminRegexTag",
                DeviceNameRegex = @"^[A-Z]{3}-\d{4}$",
                DeviceNameRegexDescription = "Must match format AAA-0000"
            };
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows
            };

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, new List<DeviceTag> { tag });

            // Assert
            Assert.IsTrue(errors.ContainsKey("PreferredHostname"));
            Assert.IsTrue(errors["PreferredHostname"].Any(e => e.Contains("Does not match name requirements")));
        }

        [TestMethod]
        [DataRow("")]
        [DataRow(null)]
        public void ValidateBulkDevice_NonAdminUser_RegexTag_EmptyHostname_ReturnsRegexError(string hostname)
        {
            // Arrange - Non-admin: DeviceRenameEnabled defaults false, regex still enforced
            var tag = new DeviceTag
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000011"),
                Name = "NonAdminRegexTag",
                DeviceNameRegex = @"^[A-Z]{3}-\d{4}$",
                DeviceNameRegexDescription = "Must match format AAA-0000"
            };
            var deviceBulk = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows
            };

            // Act
            var errors = NewDeviceValidation.ValidateBulkDevice(deviceBulk, new List<DeviceTag> { tag });

            // Assert
            Assert.IsTrue(errors.ContainsKey("PreferredHostname"));
            Assert.IsTrue(errors["PreferredHostname"].Any(e => e.Contains("Does not match name requirements")));
        }

        #endregion

    }
}