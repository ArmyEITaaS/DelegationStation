using DelegationStation.Services;
using DelegationStation.Shared;
using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Azure.Cosmos;

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
        private List<Device> AllDevices = new List<Device>();
        private List<DeviceTag> deviceTags = new List<DeviceTag>();
        private Device newDevice = new Device();
        private Role userRole = new Role() { Id = Guid.Empty, Name = "None", Attributes = new List<AllowedAttributes>() { }, SecurityGroups = false, AdministrativeUnits = false };
        private string tagSearch = "";
        private MarkupString deviceAddValidationMessage = new MarkupString("");
        private int pageSize = 10;
        private int TotalDevices = 0;
        private int TotalPages = 0;
        private string search = "";
        private Device searchDevice = new Device();
        private bool devicesLoading = true;
        private string userMessage = string.Empty;

        [Parameter] public int PageNumber { get; set; }

        private ConfirmMessage? ConfirmDelete;
        private Device deleteDevice = new Device() { Id = Guid.Empty };
        private MarkupString confirmMessage = new MarkupString("");

        private Dictionary<DeviceStatus, string> StatusDefinitions = new Dictionary<DeviceStatus, string>{   
            { DeviceStatus.Added, "Device has been added to the system but not yet synced with corporate identifiers." },
            { DeviceStatus.Synced, "Device has been successfully synced with corporate identifiers." },
            { DeviceStatus.Deleting, "Device is in the process of being deleted from the system." },
            { DeviceStatus.NotSyncing, "Device is not currently in a tag group configured to sync to corporate identifiers." }
        };

        protected override async Task OnInitializedAsync()
        {
            if (AuthState is not null)
            {
                var authState = await AuthState;
                user = authState?.User ?? new System.Security.Claims.ClaimsPrincipal();
                userName = user.Claims.Where(c => c.Type == "name").Select(c => c.Value.ToString()).FirstOrDefault() ?? "";
                userId = user.Claims.Where(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier").Select(c => c.Value.ToString()).FirstOrDefault() ?? "";
            }

            if (PageNumber < 1)
            {
                PageNumber = 1;
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
                AllDevices = await deviceDBService.GetDevicesAsync(groups);
                TotalDevices = AllDevices.Count;
                TotalPages = (int)Math.Ceiling((double)AllDevices.Count / pageSize);
                devices = GetDevicesByPage(PageNumber, pageSize);
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
        
        public List<Device> GetDevicesByPage(int pageNumber, int pageSize)
        {
            List<Device> pagedDevices = new List<Device>();

            if (AllDevices.Count <= pageSize)
            {
                return AllDevices;
            }
            else
            {
                int startIndex = (pageNumber - 1) * pageSize;
                int endIndex = Math.Min(startIndex + pageSize, AllDevices.Count);
                for (int i = startIndex; i < endIndex; i++)
                {
                    pagedDevices.Add(AllDevices[i]);
                }
            }

            return pagedDevices;

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

                AllDevices = await deviceDBService.GetDevicesSearchAsync(searchDevice.Make, searchDevice.Model, searchDevice.SerialNumber, deviceOSID, searchDevice.PreferredHostname);
                TotalDevices = AllDevices.Count;
                TotalPages = (int)Math.Ceiling((double)AllDevices.Count / pageSize);
                FirstPage();
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
            // confirmMessage = (MarkupString)$"<b>This will mark the device to be unenrolled and deleted from Corporate Identifiers and Delegation Station: </b></br></br><b>Make:</b> {deleteDevice.Make}<br /><b>Model:</b> {deleteDevice.Model}<br /><b>Serial Number:</b> {deleteDevice.SerialNumber}</br></br><b>Confirm you want to <u>unenroll</u> and <u>delete</u> this device:</b><br />";
            confirmMessage = (MarkupString)$"<b>This will mark the device to be deleted from both Corporate Identifiers and Delegation Station: </b></br></br><b>Make:</b> {deleteDevice.Make}<br /><b>Model:</b> {deleteDevice.Model}<br /><b>Serial Number:</b> {deleteDevice.SerialNumber}</br></br><b>Confirm you want to <u>delete</u> this device:</b><br />";
            ConfirmDelete?.Show();
        }

        private void FirstPage()
        {
            PageNumber = 1;
            devices = GetDevicesByPage(PageNumber, pageSize);
        }

        private void LastPage()
        {
            PageNumber = TotalPages;
            devices = GetDevicesByPage(PageNumber, pageSize);
        }
        private void NextPage()
        {
            if (PageNumber < TotalPages)
            {
                PageNumber++;
            }
            devices = GetDevicesByPage(PageNumber, pageSize);
        }

        private void PreviousPage()
        {
            if (PageNumber > 1)
            {
                PageNumber--;
            }
            devices = GetDevicesByPage(PageNumber, pageSize);
        }
    }
}
