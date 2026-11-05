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
        private string currentDeviceSearchTag = "";
        private int pageSize = 10;
        // Current 1-based page number for display and navigation
        private int PageNumber = 1;
        private int TotalDevices = 0;
        private int TotalPages = 0;
        private Device searchDevice = new Device();
        private Device activeSearchDevice = new Device();
        // Selected device state filter from the search dropdown (empty = no filter)
        private string? searchStatus;
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
            { DeviceStatus.NonSyncing, "Device is not currently in a tag group configured to sync to corporate identifiers." },
            { DeviceStatus.Failed, "Device repeatedly failed to sync to Corporate Identifiers." }
        };

        private EditContext editContext;
        private ValidationMessageStore messageStore;

        public Devices()
        {
            // Initialize EditContext in the constructor
            editContext = new EditContext(newDevice);
            editContext.OnValidationRequested += HandleValidationRequested;
            messageStore = new ValidationMessageStore(editContext);
            searchDevice.Tags = new List<string>();
            searchDevice.Tags.Add(string.Empty);
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
            // THIS WILL ALWAYS LOG WHEN FORM IS SUBMITTED - even if DataAnnotations fail
            logger?.LogWarning("=== VALIDATION TRIGGERED === SerialNumber: {SerialNumber}, Make: {Make}, Model: {Model}, Hostname: {PreferredHostname}, Tags: {TagCount}",
                newDevice.SerialNumber ?? "NULL",
                newDevice.Make ?? "NULL",
                newDevice.Model ?? "NULL",
                newDevice.PreferredHostname ?? "NULL",
                newDevice.Tags?.Count ?? 0);

            if (messageStore == null || editContext == null)
            {
                logger?.LogWarning("HandleValidationRequested: messageStore or editContext is null");
                return;
            }

            messageStore?.Clear();

            //custom validation
            logger?.LogInformation("Starting custom device validation for SerialNumber: {SerialNumber}, TagCount: {TagCount}",
                newDevice.SerialNumber, newDevice.Tags?.Count ?? 0);

            var validationErrors = Validation.NewDeviceValidation.ValidateDevice(newDevice, deviceTags, logger);

            if (validationErrors.Count > 0)
            {
                logger?.LogWarning("Validation completed with {ErrorCount} error(s) for device SerialNumber: {SerialNumber}",
                    validationErrors.Count, newDevice.SerialNumber);
            }
            else
            {
                logger?.LogInformation("Validation passed for device SerialNumber: {SerialNumber}", newDevice.SerialNumber);
            }

            foreach(var err in validationErrors)
            {
                messageStore.Add(editContext.Field(err.Key), err.Value);
                logger?.LogInformation("Validation error added - Field: {FieldName}, Errors: {@Errors}", err.Key, err.Value);
                editContext.NotifyValidationStateChanged();
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
        private List<string> GetMatchingTags()
        {
            var matchingTags = new List<string>();
            var tagFilter = currentDeviceSearchTag.Trim();

            if (tagFilter.Length == 0)
            {
                return matchingTags;
            }

            foreach (var tag in deviceTags)
            {
                if (tag.Name.Contains(tagFilter, StringComparison.OrdinalIgnoreCase))
                {
                    matchingTags.Add(tag.Id.ToString());
                }
            }

            return matchingTags;
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
            searchDevice.Tags = GetMatchingTags();
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
                activeSearchDevice.Tags = searchDevice.Tags;
                if (string.IsNullOrWhiteSpace(searchStatus) ||
                    !Enum.TryParse<DeviceStatus>(searchStatus, ignoreCase: true, out var parsedStatus))
                {
                    activeSearchDevice.Status = null;
                }
                else
                {
                    activeSearchDevice.Status = parsedStatus;
                }
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

            logger?.LogInformation("Starting AddDevice operation. SerialNumber: {SerialNumber}, CorrelationId: {CorrelationId}, User: {UserName} {UserId}",
                newDevice.SerialNumber, c, userName, userId);

            try
            {

                DeviceTag tag = deviceTags.Where(t => t.Id.ToString() == newDevice.Tags[0]).FirstOrDefault() ?? new DeviceTag();

                logger?.LogDebug("Retrieved tag {TagId} ({TagName}) and Regex = {DeviceNameRegex} for device {SerialNumber}",
                    tag.Id, tag.Name, tag.DeviceNameRegex, newDevice.SerialNumber);
                var authRequest = await authorizationService.AuthorizeAsync(user, tag, Authorization.DeviceTagOperations.Read);
                if (!authRequest.Succeeded)
                    //if (!authorizationService.AuthorizeAsync(user, tag, Authorization.DeviceTagOperations.Read).Result.Succeeded)
                {
                    userMessage = (MarkupString)$"Error: Not authorized to add devices to tag {tag.Id} {tag.Name}.\nCorrelation Id: {c.ToString()}";
                    logger.LogError("Authorization failed: User not authorized to add devices to tag {TagId} ({TagName}). CorrelationId: {CorrelationId}, User: {UserName} {UserId}",
                        tag.Id, tag.Name, c, userName, userId);
                    return;
                }

                logger?.LogDebug("Authorization successful for tag {TagId}", tag.Id);

                newDevice.ModifiedUTC = DateTime.UtcNow;
                newDevice.AddedBy = userId;

                logger?.LogInformation("Adding device to database. Make: {Make}, Model: {Model}, SerialNumber: {SerialNumber}, Tag: {TagId}, CorrelationId: {CorrelationId}",
                    newDevice.Make, newDevice.Model, newDevice.SerialNumber, tag.Id, c);

                Device resp = await deviceDBService.AddOrUpdateDeviceAsync(newDevice);
                devices.Add(resp);

                logger?.LogInformation("Device added successfully. DeviceId: {DeviceId}, SerialNumber: {SerialNumber}, CorrelationId: {CorrelationId}, User: {UserName} {UserId}",
                    resp.Id, resp.SerialNumber, c, userName, userId);

                // Reset form
                newDevice = new Device();

                //TODO:  Can we safely remove these two lines
                editContext.OnValidationRequested += HandleValidationRequested;
                messageStore = new ValidationMessageStore(editContext);

                // Create new EditContext and messageStore
                editContext = new EditContext(newDevice);
                editContext.OnValidationRequested += HandleValidationRequested;
                messageStore = new ValidationMessageStore(editContext);

                logger?.LogDebug("Form reset completed");

                userMessage = (MarkupString)$"Device added successfully.";
            }
            catch (Exception ex)
            {
                userMessage = (MarkupString)$"Error adding device: {ex.Message} <br />Correlation Id:{c.ToString()}";
                logger.LogError(ex, "Error adding device. SerialNumber: {SerialNumber}, CorrelationId: {CorrelationId}, User: {UserName} {UserId}",
                    newDevice.SerialNumber, c, userName, userId);
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
