using DelegationStation.Services;
using DelegationStation.Shared;
using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace DelegationStation.Pages
{
    public partial class Devices
    {
        [CascadingParameter]
        public Task<AuthenticationState>? AuthState { get; set; }
        private System.Security.Claims.ClaimsPrincipal user = new System.Security.Claims.ClaimsPrincipal();
        private string userId = string.Empty;
        private string userName = string.Empty;

        private List<string> groups = new List<string>();
        private List<Device> devices = new List<Device>();
        private List<DeviceTag> deviceTags = new List<DeviceTag>();
        private Device newDevice = new Device();
        private Role userRole = new Role() { Id = Guid.Empty, Name = "None", Attributes = new List<AllowedAttributes>() { }, SecurityGroups = false, AdministrativeUnits = false };
        private string tagSearch = "";
        private MarkupString deviceAddValidationMessage = new MarkupString("");
        private int pageSize = 10;
        private int currentPage = 0;
        private string search = "";
        private Device searchDevice = new Device();
        private bool devicesLoading = true;
        private string userMessage = string.Empty;

        private ConfirmMessage? ConfirmDelete;
        private Device deleteDevice = new Device() { Id = Guid.Empty };
        private MarkupString confirmMessage = new MarkupString("");

        protected override async Task OnInitializedAsync()
        {
            if (AuthState is not null)
            {
                var authState = await AuthState;
                user = authState?.User ?? new System.Security.Claims.ClaimsPrincipal();
                userName = user.Claims.Where(c => c.Type == "name").Select(c => c.Value.ToString()).FirstOrDefault() ?? "";
                userId = user.Claims.Where(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier").Select(c => c.Value.ToString()).FirstOrDefault() ?? "";
            }

            UpdateClaims();
            await GetTags();
            await GetDevices();
        }

        private void UpdateClaims()
        {
            groups = new List<string>();
            var groupClaims = user.Claims.Where(c => c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role" || c.Type == "roles");

            if (groupClaims == null)
            {
                return;
            }

            foreach (var c in groupClaims)
            {
                groups.Add(c.Value);
            }
        }

        private async Task GetTags()
        {
            Guid c = Guid.NewGuid();
            userMessage = string.Empty;
            try
            {
                deviceTags = await deviceTagDBService.GetDeviceTagsAsync(groups);
            }
            catch (Exception ex)
            {
                userMessage = $"Error retrieving tags.\nCorrelation Id: {c.ToString()}";
                logger.LogError($"{userMessage}\n{ex.Message}\nUser: {userName} {userId}");
            }
        }

        private async Task GetDevices()
        {
            Guid c = Guid.NewGuid();
            userMessage = string.Empty;
            try
            {
                devices = await deviceDBService.GetDevicesAsync(groups, search, pageSize, currentPage);
            }
            catch (Exception ex)
            {
                userMessage = $"Error retrieving Devices.\nCorrelation Id: {c.ToString()}";
                logger.LogError($"{userMessage}\n{ex.Message}\nUser: {userName} {userId}");
            }
            finally
            {
                devicesLoading = false;
            }
        }

        private async Task GetDevicesSearch()
        {
            Guid c = Guid.NewGuid();
            userMessage = string.Empty;
            try
            {
                int? deviceOSID = null;
                if (searchDevice.OS != null)
                {
                    deviceOSID = (int) searchDevice.OS;
                }

                devices = await deviceDBService.GetDevicesSearchAsync(searchDevice.Make, searchDevice.Model, searchDevice.SerialNumber, deviceOSID, searchDevice.PreferredHostName);
            }
            catch (Exception ex)
            {
                userMessage = $"Error retrieving searching Devices.\nCorrelation Id: {c.ToString()}";
                logger.LogError($"{userMessage}\n{ex.Message}\nUser: {userName} {userId}");
            }
        }

        private async Task AddDevice()
        {
            Guid c = Guid.NewGuid();
            userMessage = string.Empty;
            try
            {
                if (newDevice.Tags.Count < 1)
                {
                    deviceAddValidationMessage = (MarkupString)"Device must have at least one Tag";
                    return;
                }
                else if (newDevice.Tags.Count > 1)
                {
                    deviceAddValidationMessage = (MarkupString)"Device must only have one Tag";
                    return;
                }
                DeviceTag tag = deviceTags.Where(t => t.Id.ToString() == newDevice.Tags[0]).FirstOrDefault() ?? new DeviceTag();
                if (!authorizationService.AuthorizeAsync(user, tag, Authorization.DeviceTagOperations.Read).Result.Succeeded)
                {
                    userMessage = $"Error: Not authorized to add devices to tag {tag.Id} {tag.Name}.\nCorrelation Id: {c.ToString()}";
                    logger.LogError($"{userMessage}\nUser: {userName} {userId}");
                    return;
                }

                newDevice.ModifiedUTC = DateTime.UtcNow;
                newDevice.AddedBy = userId;
                Device resp = await deviceDBService.AddOrUpdateDeviceAsync(newDevice);
                devices.Add(resp);
                newDevice = new Device();
                deviceAddValidationMessage = (MarkupString)"";
            }
            catch (Exception ex)
            {
                deviceAddValidationMessage = (MarkupString)$"Error adding device: {ex.Message} <br />Correlation Id:{c.ToString()}";
                logger.LogError($"{deviceAddValidationMessage}\n{ex.Message}\nUser: {userName} {userId}");
            }
        }

        private void AddRemoveTag(DeviceTag tag)
        {
            if (newDevice.Tags.Contains(tag.Id.ToString()))
            {
                newDevice.Tags.Remove(tag.Id.ToString());
            }
            else
            {
                newDevice.Tags.Clear();
                newDevice.Tags.Add(tag.Id.ToString());
            }
        }

        private void RemoveDevice(Device device)
        {
            deleteDevice = device;
            Show();
        }

        private async Task DeleteDevice()
        {
            Guid c = Guid.NewGuid();
            if (deleteDevice.Id == Guid.Empty)
            {
                return;
            }

            try
            {
                await deviceDBService.MarkDeviceToDeleteAsync(deleteDevice);
                string message = $"Correlation Id: {c.ToString()}\nDevice {deleteDevice.Make} {deleteDevice.Model} {deleteDevice.SerialNumber} deleted successfully";
                userMessage = "";
                logger.LogInformation($"{message}\nUser: {userName} {userId}");
            }
            catch (Exception ex)
            {
                string message = $"Error deleting device {deleteDevice.Make} {deleteDevice.Model} {deleteDevice.SerialNumber}.\nCorrelation Id: {c.ToString()}";
                logger.LogError(ex, $"{message}\nUser: {userName} {userId}");
                userMessage = message;
            }
            deleteDevice = new Device() { Id = Guid.Empty };
            confirmMessage = (MarkupString)"";
            StateHasChanged();
        }

        private void Show()
        {
            confirmMessage = (MarkupString)$"<b>This will mark the device to be unenrolled and deleted from Corporate Identifiers and Delegation Station: </b></br></br><b>Make:</b> {deleteDevice.Make}<br /><b>Model:</b> {deleteDevice.Model}<br /><b>Serial Number:</b> {deleteDevice.SerialNumber}</br></br><b>Confirm you want to <u>unenroll</u> and <u>delete</u> this device:</b><br />";
            ConfirmDelete?.Show();
        }
    }
}
