using DelegationStation.Shared;
using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;

namespace DelegationStation.Pages
{
    public partial class Devices : IDisposable
    {
        [CascadingParameter]
        public Task<AuthenticationState>? AuthState { get; set; }
        private System.Security.Claims.ClaimsPrincipal user = new System.Security.Claims.ClaimsPrincipal();
        private string userId = string.Empty;
        private string userName = string.Empty;

        private List<string> groups = new List<string>();
        private List<Device> devices = new List<Device>();
        //private List<Device> AllDevices = new List<Device>();
        private List<DeviceTag> deviceTags = new List<DeviceTag>();

        private Device newDevice = new Device();

        private Role userRole = new Role() { Id = Guid.Empty, Name = "None", Attributes = new List<AllowedAttributes>() { }, SecurityGroups = false, AdministrativeUnits = false };
        private string tagSearch = "";
        private int pageSize = 10;
        // Current 1-based page number for display and navigation
        private int PageNumber = 1;
        private int TotalDevices = 0;
        private int TotalPages = 0;
        private Device searchDevice = new Device();
        private Device activeSearchDevice = new Device();
        // Tracks whether the user's last action was a search (true) or a default page load (false)
        private bool isSearchActive = false;
        private bool devicesLoading = true;
        private MarkupString userMessage = new MarkupString("");

        //[Parameter] public int PageNumber { get; set; }

        private ConfirmMessage? ConfirmDelete;
        private Device deleteDevice = new Device() { Id = Guid.Empty };
        private MarkupString confirmMessage = new MarkupString("");

        private Dictionary<DeviceStatus, string> StatusDefinitions = new Dictionary<DeviceStatus, string>{
            { DeviceStatus.Added, "Device has been added to the system but not yet synced with corporate identifiers." },
            { DeviceStatus.Synced, "Device has been successfully synced with corporate identifiers." },
            { DeviceStatus.Deleting, "Device is in the process of being deleted from the system." },
            { DeviceStatus.NotSyncing, "Device is not currently in a tag group configured to sync to corporate identifiers." }
        };

        private EditContext editContext;
        private ValidationMessageStore messageStore;

        public Devices()
        {
            // Initialize EditContext in the constructor
            editContext = new EditContext(newDevice);
            editContext.OnValidationRequested += HandleValidationRequested;
            messageStore = new ValidationMessageStore(editContext);
        }

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

        private void HandleValidationRequested(object? sender, ValidationRequestedEventArgs args)
        {
            if (messageStore == null || editContext == null)
                return;

            messageStore?.Clear();

            //custom validation
            var validationErrors = Validation.NewDeviceValidation.ValidateDevice(newDevice, deviceTags);

            foreach(var err in validationErrors)
            {
                messageStore.Add(editContext.Field(err.Key), err.Value);
            }


        }

        public void Dispose()
        {
            if (editContext is not null)
            {
                editContext.OnValidationRequested -= HandleValidationRequested;
            }
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
            userMessage = new MarkupString("");

            try
            {
                deviceTags = await deviceTagDBService.GetDeviceTagsAsync(groups);
            }
            catch (Exception ex)
            {
                userMessage = (MarkupString)$"Error retrieving tags.\nCorrelation Id: {c.ToString()}";
                logger.LogError($"{userMessage}\n{ex.Message}\nUser: {userName} {userId}");
            }
        }
        private async Task GetDevices()
        {
            Guid c = Guid.NewGuid();
            userMessage = new MarkupString("");
            // Default mode: clear any active search state
            isSearchActive = false;
            activeSearchDevice = new Device();
            try
            {
                // Fetch total device count to compute total pages for pagination                
                TotalDevices = await deviceDBService.GetDeviceSearchCountAsync(
                    groups, activeSearchDevice);
                TotalPages = (int)Math.Ceiling((decimal)TotalDevices / pageSize);

                // Lazy load only the current page of devices (0-based page index)
                devices = await deviceDBService.GetDevicesAsync(groups, activeSearchDevice, pageSize, PageNumber - 1);
            }
            catch (Exception ex)
            {
                userMessage = (MarkupString)$"Error retrieving Devices.\nCorrelation Id: {c.ToString()}";
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
            userMessage = new MarkupString("");
            devicesLoading = true;

            try
            {                
                // Reset to page 1 whenever a new search is initiated
                PageNumber = 1;
                isSearchActive = true;
                activeSearchDevice.OS = searchDevice.OS;
                activeSearchDevice.SerialNumber = searchDevice.SerialNumber;
                activeSearchDevice.Make = searchDevice.Make;
                activeSearchDevice.Model = searchDevice.Model;
                activeSearchDevice.PreferredHostname = searchDevice.PreferredHostname;
                // Fetch the total count of matching devices to compute pagination
                TotalDevices = await deviceDBService.GetDeviceSearchCountAsync(
                    groups, activeSearchDevice);
                TotalPages = (int)Math.Ceiling((decimal)TotalDevices / pageSize);

                // Lazy load only the first page of search results
                devices = await deviceDBService.GetDevicesSearchAsync(
                    groups, activeSearchDevice, pageSize, PageNumber - 1);
            }
            catch (Exception ex)
            {
                userMessage = (MarkupString)$"Error searching Devices.\nCorrelation Id: {c.ToString()}";
                logger.LogError($"{userMessage}\n{ex.Message}\nUser: {userName} {userId}");
            }
            finally
            {
                devicesLoading = false;
            }
        }

        /// <summary>
        /// Reloads only the device list for the current page without resetting PageNumber.
        /// Respects the current mode: uses search parameters when a search is active,
        /// otherwise falls back to the default group-filtered device load.
        /// </summary>
        private async Task ReloadCurrentPage()
        {
            if (isSearchActive)
            {
                Guid c = Guid.NewGuid();
                try
                {
                    // Fetch only the requested page of search results
                    devices = await deviceDBService.GetDevicesSearchAsync(
                        groups, activeSearchDevice, pageSize, PageNumber - 1);
                }
                catch (Exception ex)
                {
                    userMessage = (MarkupString)$"Error retrieving Devices.\nCorrelation Id: {c.ToString()}";
                    logger.LogError($"{userMessage}\n{ex.Message}\nUser: {userName} {userId}");
                }
            }
            else
            {
                // Default mode: delegate to GetDevices() which handles count + page fetch
                await GetDevices();
            }
        }

        private async Task AddDevice()
        {
            Guid c = Guid.NewGuid();
            userMessage = new MarkupString("");

            try
            {

                DeviceTag tag = deviceTags.Where(t => t.Id.ToString() == newDevice.Tags[0]).FirstOrDefault() ?? new DeviceTag();
                if (!authorizationService.AuthorizeAsync(user, tag, Authorization.DeviceTagOperations.Read).Result.Succeeded)
                {
                    userMessage = (MarkupString)$"Error: Not authorized to add devices to tag {tag.Id} {tag.Name}.\nCorrelation Id: {c.ToString()}";
                    logger.LogError($"{userMessage}\nUser: {userName} {userId}");
                    return;
                }

                newDevice.ModifiedUTC = DateTime.UtcNow;
                newDevice.AddedBy = userId;
                Device resp = await deviceDBService.AddOrUpdateDeviceAsync(newDevice);
                devices.Add(resp);

                // Reset form
                newDevice = new Device();

                //TODO:  Can we safely remove these two lines
                editContext.OnValidationRequested += HandleValidationRequested;
                messageStore = new ValidationMessageStore(editContext);

                // Create new EditContext and messageStore
                editContext = new EditContext(newDevice);
                editContext.OnValidationRequested += HandleValidationRequested;
                messageStore = new ValidationMessageStore(editContext);

                userMessage = (MarkupString)$"Device added successfully.";
            }
            catch (Exception ex)
            {
                userMessage = (MarkupString)$"Error adding device: {ex.Message} <br />Correlation Id:{c.ToString()}";
                logger.LogError($"{userMessage}\n{ex.Message}\nUser: {userName} {userId}");
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

            // Clear validation messages when tag selection changes
            messageStore.Clear();

            // Notify EditContext that validation state has changed
            editContext.NotifyValidationStateChanged();
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
                //userMessage = "";
                userMessage = (MarkupString)"";
                logger.LogInformation($"{message}\nUser: {userName} {userId}");
            }
            catch (Exception ex)
            {
                string message = $"Error deleting device {deleteDevice.Make} {deleteDevice.Model} {deleteDevice.SerialNumber}.\nCorrelation Id: {c.ToString()}";
                logger.LogError(ex, $"{message}\nUser: {userName} {userId}");
                userMessage = (MarkupString)message;
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

        /// <summary>Navigates to the first page and reloads devices.</summary>
        private async Task FirstPage()
        {
            PageNumber = 1;
            await ReloadCurrentPage();
        }

        /// <summary>Navigates to the last page and reloads devices.</summary>
        private async Task LastPage()
        {
            PageNumber = TotalPages > 0 ? TotalPages : 1;
            await ReloadCurrentPage();
        }

        /// <summary>Navigates to the next page if one exists and reloads devices.</summary>
        private async Task NextPage()
        {
            if (PageNumber < TotalPages)
            {
                PageNumber++;
            }
            await ReloadCurrentPage();
        }

        /// <summary>Navigates to the previous page if one exists and reloads devices.</summary>
        private async Task PreviousPage()
        {
            if (PageNumber > 1)
            {
                PageNumber--;
            }
            await ReloadCurrentPage();
        }
    }
}
