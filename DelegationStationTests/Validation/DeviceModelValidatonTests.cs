using System.ComponentModel.DataAnnotations;

namespace DelegationStationTests.Validation
{
    [TestClass]
    public class DeviceModelValidationTests
    {

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
            var device = new Device
            {
                Make = make,
                Model = "Model",
                SerialNumber = "00000"
            };
            Assert.IsFalse(ValidateModel(device).Any(
                v => (v.ErrorMessage ?? "").Contains("Only use")));

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
        [DataRow("InvalidMake123+")]
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
            var device = new Device
            {
                Make = make,
                Model = "Model",
                SerialNumber = "00000"
            };
            Assert.IsTrue(ValidateModel(device).Any(
                v => (v.ErrorMessage ?? "").Contains("Only use")));

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
            var device = new Device
            {
                Make = "Make",
                Model = model,
                SerialNumber = "00000"
            };
            Assert.IsFalse(ValidateModel(device).Any(
                v => (v.ErrorMessage ?? "").Contains("Only use")));

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
            var device = new Device
            {
                Make = "Make",
                Model = model,
                SerialNumber = "00000"
            };
            Assert.IsTrue(ValidateModel(device).Any(
                v => (v.ErrorMessage ?? "").Contains("Only use")));

        }

        [TestMethod]
        [DataRow("ValidSN123")]
        [DataRow("ValidSN123-")]
        [DataRow("ValidSN123_")]
        [DataRow("ValidSN123.")]
        public void VerifyValidSerialNumberAllowed(string serialNumber)
        {
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = serialNumber
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
        [DataRow("InvalidSN123+")]
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
            var device = new Device
            {
                Make = "Make",
                Model = "Model",
                SerialNumber = serialNumber
            };
            Assert.IsTrue(ValidateModel(device).Any(
                v => (v.ErrorMessage ?? "").Contains("Only use")));

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