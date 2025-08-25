using DelegationStation.Interfaces;
using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace DelegationStation.API
{
    [ApiController]
    [Route("api")]
    [Authorize]
    public class BulkDeviceController : Controller
    {
        private readonly ILogger _logger;
        private IDeviceDBService _deviceDBService;
        private IDeviceTagDBService _deviceTagDBService;
        private readonly IConfiguration _config;
        private readonly IAuthorizationService _authorizationService;
        public BulkDeviceController(IDeviceDBService deviceService, IDeviceTagDBService deviceTagDBService, IConfiguration config, ILogger<BulkDeviceController> logger, IAuthorizationService authorizationService)
        {
            _logger = logger;
            _deviceDBService = deviceService;
            _deviceTagDBService = deviceTagDBService;
            _config = config;
            _authorizationService = authorizationService;
        }

        [HttpGet("BulkDevice")]
        public async Task<IActionResult> Download(string id = "")
        {

            // There's other validation done later on in the code prior to the database call, which I didn't remove since it's called by other code
            // But wanted to ensure we do validation as close to the call as possible
            string sanitizedID = validateInput(id);
            if (sanitizedID == "")
            {
                return BadRequest("Invalid tag id provided");
            }

            List<string> groups = new List<string>();
            var roleClaims = User.Claims.Where(c => c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role");
            roleClaims = roleClaims ?? new List<System.Security.Claims.Claim>();
            foreach (var c in roleClaims)
            {
                groups.Add(c.Value);
            }
            Role userRole = new Role();
            string defaultGroup = _config.GetSection("DefaultAdminGroupObjectId").Value ?? "";


            DeviceTag? tag = null;
            try
            {
                tag = await _deviceTagDBService.GetDeviceTagAsync(sanitizedID);
            }
            catch (Exception ex)
            {
                _logger.LogError($"BulkDeviceController Download error getting tag {sanitizedID}.\nError: {ex.Message}");
                return BadRequest("Unable to find tag");
            }

            if (tag == null)
            {
                return BadRequest("Unable to find tag");
            }

            if (_authorizationService.AuthorizeAsync(User, tag, Authorization.DeviceTagOperations.Read).Result.Succeeded == false)
            {
                return new UnauthorizedResult();
            }

            string fileName = "Devices.csv";
            List<Device> devices = await _deviceDBService.GetDevicesByTagAsync(sanitizedID);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Make,Model,SerialNumber,OS,PreferredHostname,Action,AddedBy");
            
            if (!string.IsNullOrEmpty(sanitizedID))
            {
                string deviceOSstring = "";

                foreach (Device device in devices)
                {
                    if (device.Make.Contains(","))
                    {
                        device.Make = "\"" + device.Make + "\"";
                    }
                    if (device.Model.Contains(","))
                    {
                        device.Model = "\"" + device.Model + "\"";
                    }
                    if (device.SerialNumber.Contains(","))
                    {
                        device.SerialNumber = "\"" + device.SerialNumber + "\"";
                    }
                    if( device.OS == null )
                    {
                        deviceOSstring = "-- unknown --";
                    }
                    else
                    {
                        deviceOSstring = Enum.GetName(typeof(DeviceOS), device.OS) ?? "";
                    }

                    sb.AppendLine($"{device.Make},{device.Model},{device.SerialNumber},{deviceOSstring},{device.PreferredHostname},,{device.AddedBy}");
                }
            }

            byte[] fileBytes = Encoding.ASCII.GetBytes(sb.ToString());

            return File(fileBytes, "text/csv", fileName);
        }

        public string validateInput(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                _logger.LogError("BulkDeviceController Download error: Tag Id Empty");
                return "";
            }

            if (!System.Text.RegularExpressions.Regex.Match(id, DSConstants.GUID_REGEX).Success)
            {
                // Protecting against log injection
                string loggableID = id.Replace("\n", "").Replace("\r", "").Replace("\t", "");
                _logger.LogError($"BulkDeviceController Download error: Tag Id provided is not a valid GUID: {loggableID}");
                return "";
            }

            return id.Replace("\n", "").Replace("\r", "").Replace("\t", "");
        }


    }
}
