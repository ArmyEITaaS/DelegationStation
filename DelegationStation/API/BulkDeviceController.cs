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
            List<string> groups = new List<string>();
            var roleClaims = User.Claims.Where(c => c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role");
            roleClaims = roleClaims ?? new List<System.Security.Claims.Claim>();
            foreach (var c in roleClaims)
            {
                groups.Add(c.Value);
            }
            Role userRole = new Role();
            string defaultGroup = _config.GetSection("DefaultAdminGroupObjectId").Value ?? "";

            if (string.IsNullOrEmpty(id))
            {
                return BadRequest("Tag Id Empty");
            }

            DeviceTag? tag = null;
            try
            {
                tag = await _deviceTagDBService.GetDeviceTagAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogError($"BulkDeviceController Download error getting tag {id}.\nError: {ex.Message}");
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
            List<Device> devices = await _deviceDBService.GetDevicesByTagAsync(id);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Make,Model,SerialNumber,OS,PreferredHostName,Action,AddedBy");
            if (!string.IsNullOrEmpty(id))
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

                    sb.AppendLine($"{device.Make},{device.Model},{device.SerialNumber},{deviceOSstring},{device.PreferredHostName},,{device.AddedBy}");
                }
            }

            byte[] fileBytes = Encoding.ASCII.GetBytes(sb.ToString());

            return File(fileBytes, "text/csv", fileName);
        }
    }
}
