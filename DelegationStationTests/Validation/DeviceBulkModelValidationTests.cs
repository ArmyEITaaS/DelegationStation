using DelegationStationShared.Enums;
using System.ComponentModel.DataAnnotations;


namespace DelegationStationTests.Validation
{
    [TestClass]
    public class DeviceBulkModelValidationTests
    {

        [TestMethod]
        public void VerifyMakeIsRequired()
        {
            var device = new DeviceBulk
            {
                Make = "",
                Model = "Model",
                SerialNumber = "99999",
                PreferredHostname = "hostname",
                OS = DeviceOS.Windows
            };

            Assert.IsTrue(ValidateModel(device).Any(
                v => (v.ErrorMessage ?? "").Contains("required")));
        }

        [TestMethod]
        public void VerifyModelIsRequired()
        {
            var device = new DeviceBulk
            {
                Make = "Make",
                Model = "",
                SerialNumber = "99999",
                PreferredHostname = "hostname",
                OS = DeviceOS.Windows
            };

            Assert.IsTrue(ValidateModel(device).Any(
                v => (v.ErrorMessage ?? "").Contains("required")));
        }

        [TestMethod]
        public void VerifySNIsRequired()
        {
            var device = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "",
                PreferredHostname = "hostname",
                OS = DeviceOS.Windows
            };

            Assert.IsTrue(ValidateModel(device).Any(
                v => (v.ErrorMessage ?? "").Contains("required")));
        }

        [TestMethod]
        [DataRow("ValidMake123")]
        [DataRow("ValidMake123-")]
        [DataRow("ValidMake123_")]
        [DataRow("ValidMake123.")]
        [DataRow("ValidMake123,")]
        [DataRow("ValidMake123&")]
        [DataRow("ValidMake123(")]
        [DataRow("ValidMake123)")]
        public void VerifyValidMakeAllowed(string make)
        {
            var device = new DeviceBulk
            {
                Make = make,
                Model = "Model",
                SerialNumber = "00000",
                PreferredHostname = "hostname",
                OS = DeviceOS.Windows
            };
            Assert.IsFalse(ValidateModel(device).Any(
                v => (v.ErrorMessage ?? "").Contains("only use")));
        }

        [TestMethod]
        [DataRow("InvalidMake123!")]
        [DataRow("InvalidMake123@")]
        [DataRow("InvalidMake123#")]
        [DataRow("InvalidMake123$")]
        [DataRow("InvalidMake123%")]
        [DataRow("InvalidMake123^")]
        [DataRow("InvalidMake123*")]
        [DataRow("InvalidMake123+")]
        [DataRow("InvalidMake123=")]
        [DataRow("InvalidMake123{")]
        [DataRow("InvalidMake123}")]
        [DataRow("InvalidMake123[")]
        [DataRow("InvalidMake123]")]
        [DataRow("InvalidMake123\\")]
        [DataRow("InvalidMake123/")]
        [DataRow("InvalidMake123|")]
        [DataRow("InvalidMake123?")]
        [DataRow("InvalidMake123<")]
        [DataRow("InvalidMake123>")]
        [DataRow("InvalidMake123~")]
        [DataRow("InvalidMake123'")]
        [DataRow("InvalidMake123\"")]
        [DataRow("InvalidMake123`")]
        public void VerifyInvalidMakeNotAllowed(string make)
        {
            var device = new DeviceBulk
            {
                Make = make,
                Model = "Model",
                SerialNumber = "00000",
                PreferredHostname = "hostname",
                OS = DeviceOS.Windows
            };
            Assert.IsTrue(ValidateModel(device).Any(
                v => (v.ErrorMessage ?? "").Contains("only use")));
        }

        [TestMethod]
        [DataRow("ValidModel123")]
        [DataRow("ValidModel123-")]
        [DataRow("ValidModel123_")]
        [DataRow("ValidModel123.")]
        [DataRow("ValidModel123,")]
        [DataRow("ValidModel123&")]
        [DataRow("ValidModel123(")]
        [DataRow("ValidModel123)")]
        [DataRow("ValidModel123+")]
        public void VerifyValidModelAllowed(string model)
        {
            var device = new DeviceBulk
            {
                Make = "Make",
                Model = model,
                SerialNumber = "00000",
                PreferredHostname = "hostname",
                OS = DeviceOS.Windows
            };
            Assert.IsFalse(ValidateModel(device).Any(
                v => (v.ErrorMessage ?? "").Contains("only use")));
        }

        [TestMethod]
        [DataRow("InvalidModel123!")]
        [DataRow("InvalidModel123@")]
        [DataRow("InvalidModel123#")]
        [DataRow("InvalidModel123$")]
        [DataRow("InvalidModel123%")]
        [DataRow("InvalidModel123^")]
        [DataRow("InvalidModel123*")]
        [DataRow("InvalidModel123=")]
        [DataRow("InvalidModel123{")]
        [DataRow("InvalidModel123}")]
        [DataRow("InvalidModel123[")]
        [DataRow("InvalidModel123]")]
        [DataRow("InvalidModel123\\")]
        [DataRow("InvalidModel123/")]
        [DataRow("InvalidModel123|")]
        [DataRow("InvalidModel123?")]
        [DataRow("InvalidModel123<")]
        [DataRow("InvalidModel123>")]
        [DataRow("InvalidModel123~")]
        [DataRow("InvalidModel123'")]
        [DataRow("InvalidModel123\"")]
        [DataRow("InvalidModel123`")]
        public void VerifyInvalidModelNotAllowed(string model)
        {
            var device = new DeviceBulk
            {
                Make = "Make",
                Model = model,
                SerialNumber = "00000",
                PreferredHostname = "hostname",
                OS = DeviceOS.Windows
            };
            Assert.IsTrue(ValidateModel(device).Any(
                v => (v.ErrorMessage ?? "").Contains("only use")));
        }

        [TestMethod]
        [DataRow("ValidSN123")]
        [DataRow("ValidSN123-")]
        [DataRow("ValidSN123_")]
        [DataRow("ValidSN123.")]
        public void VerifyValidSerialNumberAllowed(string serialNumber)
        {
            var device = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = serialNumber,
                PreferredHostname = "hostname",
                OS = DeviceOS.Windows
            };
            Assert.IsFalse(ValidateModel(device).Any(
                v => (v.ErrorMessage ?? "").Contains("Only use")));
        }

        [TestMethod]
        [DataRow("InvalidSN123,")]
        [DataRow("InvalidSN123&")]
        [DataRow("InvalidSN123(")]
        [DataRow("InvalidSN123)")]
        [DataRow("InvalidSN123+")]
        [DataRow("InvalidSN123!")]
        [DataRow("InvalidSN123@")]
        [DataRow("InvalidSN123#")]
        [DataRow("InvalidSN123$")]
        [DataRow("InvalidSN123%")]
        [DataRow("InvalidSN123^")]
        [DataRow("InvalidSN123*")]
        [DataRow("InvalidSN123=")]
        [DataRow("InvalidSN123{")]
        [DataRow("InvalidSN123}")]
        [DataRow("InvalidSN123[")]
        [DataRow("InvalidSN123]")]
        [DataRow("InvalidSN123\\")]
        [DataRow("InvalidSN123/")]
        [DataRow("InvalidSN123|")]
        [DataRow("InvalidSN123?")]
        [DataRow("InvalidSN123<")]
        [DataRow("InvalidSN123>")]
        [DataRow("InvalidSN123~")]
        [DataRow("InvalidSN123'")]
        [DataRow("InvalidSN123\"")]
        [DataRow("InvalidSN123`")]
        public void VerifyInvalidSNNotAllowed(string serialNumber)
        {
            var device = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = serialNumber,
                PreferredHostname = "hostname",
                OS = DeviceOS.Windows
            };
            Assert.IsTrue(ValidateModel(device).Any(
                v => (v.ErrorMessage ?? "").Contains("Only use")));
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("ValidHostname123")]
        [DataRow("valid-hostname")]
        [DataRow("valid-host-name")]
        public void VerifyValidHostnameAllowed(string hostname)
        {
            var device = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows

            };
            Assert.IsFalse(ValidateModel(device).Any(
                v => (v.ErrorMessage ?? "").Contains("Only use")));

        }

        [TestMethod]
        [DataRow("InvalidHostname123,")]
        [DataRow("InvalidHostname123&")]
        [DataRow("InvalidHostname123(")]
        [DataRow("InvalidHostname123)")]
        [DataRow("InvalidHostname123+")]
        [DataRow("InvalidHostname123!")]
        [DataRow("InvalidHostname123@")]
        [DataRow("InvalidHostname123#")]
        [DataRow("InvalidHostname123$")]
        [DataRow("InvalidHostname123%")]
        [DataRow("InvalidHostname123^")]
        [DataRow("InvalidHostname123*")]
        [DataRow("InvalidHostname123=")]
        [DataRow("InvalidHostname123{")]
        [DataRow("InvalidHostname123}")]
        [DataRow("InvalidHostname123[")]
        [DataRow("InvalidHostname123]")]
        [DataRow("InvalidHostname123\\")]
        [DataRow("InvalidHostname123/")]
        [DataRow("InvalidHostname123|")]
        [DataRow("InvalidHostname123?")]
        [DataRow("InvalidHostname123<")]
        [DataRow("InvalidHostname123>")]
        [DataRow("InvalidHostname123~")]
        [DataRow("InvalidHostname123'")]
        [DataRow("InvalidHostname123\"")]
        [DataRow("InvalidHostname123`")]
        [DataRow("-InvalidHostname123")]
        [DataRow("InvalidHostname123-")]
        public void VerifyInvalidHostnameNotAllowed(string hostname)
        {
            var device = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows
            };

            // Only testing regex in this test
            Assert.IsTrue(ValidateModel(device).Any(
                v => (v.ErrorMessage ?? "").Contains("Only use")));

        }

        [TestMethod]
        [DataRow("HostnameTooLong1")]
        public void VerifyHostnameLengthValidation(string hostname)
        {
            var device = new DeviceBulk
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = "12345",
                PreferredHostname = hostname,
                OS = DeviceOS.Windows
            };
            // Only testing length in this test
            Assert.IsTrue(ValidateModel(device).Any(
                v => (v.ErrorMessage ?? "").Contains("cannot exceed 15")));
        }

        private IList<ValidationResult> ValidateModel(object model)
        {
            var validationResults = new List<ValidationResult>();
            var ctx = new ValidationContext(model, null, null);
            Validator.TryValidateObject(model, ctx, validationResults, true);
            return validationResults;
        }
    }
}
