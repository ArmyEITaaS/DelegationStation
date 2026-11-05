using DelegationStationShared.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;

namespace DelegationStation.Pages
{
    public partial class SystemStatus
    {
        [CascadingParameter]
        public Task<AuthenticationState>? AuthState { get; set; }
        private System.Security.Claims.ClaimsPrincipal user = new System.Security.Claims.ClaimsPrincipal();
        private string userId = string.Empty;
        private string userName = string.Empty;

        private List<string> groups = new List<string>();
        private List<DeviceTag> deviceTags = new List<DeviceTag>();
        private Dictionary<Guid, int> corpIDCounts = new Dictionary<Guid, int>();
        private Dictionary<Guid, int> unsyncedCounts = new Dictionary<Guid, int>();
        private string tagSearchName = string.Empty;
        private string activeTagSearchName = string.Empty;
        private string userMessage = string.Empty;
        private bool tagsLoading = true;
        private int TotalTags = 0;
        private int TotalPages = 0;
        private int PageSize = 10;

        [Parameter] public int PageNumber { get; set; }

        private const int DefaultMaxCorpIDsAllowed = 10000;

        private bool loading = true;
        private string errorMessage = string.Empty;

        private int MaxCorpIDsAllowed { get; set; } = DefaultMaxCorpIDsAllowed;
        private int CorpIDCount { get; set; } = 0;

        // If the count exceeds the limit, display it as being at the limit.
        private int DisplayCount => Math.Min(CorpIDCount, MaxCorpIDsAllowed);

        private int AvailableCorpIDSlots => Math.Max(MaxCorpIDsAllowed - CorpIDCount, 0);

        // Percentage of the max being utilized, capped at 100%.
        private double DisplayPercentage
        {
            get
            {
                if (MaxCorpIDsAllowed <= 0)
                {
                    return 0;
                }

                double percentage = (double)CorpIDCount / MaxCorpIDsAllowed * 100;
                return Math.Min(percentage, 100);
            }
        }

        // RED at or above 100%, Yellow above 90%, Green otherwise.
        private string UtilizationCssClass
        {
            get
            {
                if (MaxCorpIDsAllowed <= 0)
                {
                    return "text-success fw-bold";
                }

                double actualPercentage = (double)CorpIDCount / MaxCorpIDsAllowed * 100;
                if (actualPercentage >= 100)
                {
                    return "text-danger fw-bold";
                }
                if (actualPercentage > 90)
                {
                    return "text-warning fw-bold";
                }
                return "text-success fw-bold";
            }
        }

        protected override async Task OnInitializedAsync()
        {
            try
            {
                MaxCorpIDsAllowed = GetMaxCorpIDsAllowed();

                CorpIDCounter? counter = await corpIdDBService.GetCorpIDCounterAsync();
                if (counter == null)
                {
                    errorMessage = "Corporate Identifier counter was not found in the database.";
                }
                else
                {
                    CorpIDCount = counter.CorpIDCount;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load Corporate Identifier status.");
                errorMessage = "Failed to load Corporate Identifier status.";
            }
            finally
            {
                loading = false;
            }
            if (AuthState is not null)
            {
                var authState = await AuthState;
                user = authState?.User ?? new System.Security.Claims.ClaimsPrincipal();
                userName = user.Claims.Where(c => c.Type == "name").Select(c => c.Value.ToString()).FirstOrDefault() ?? "";
                userId = user.Claims.Where(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier").Select(c => c.Value.ToString()).FirstOrDefault() ?? "";
            }

            PageSize = deviceTagDBService.CurrentSearch.pageSize;
            PageNumber = deviceTagDBService.CurrentSearch.pageNumber;
            tagSearchName = deviceTagDBService.CurrentSearch.name ?? string.Empty;
            activeTagSearchName = tagSearchName;
            UpdateClaims();
            await GetTags();
        }

        private int GetMaxCorpIDsAllowed()
        {
            string? maxCorpIDsString = configuration["MAX_CORPIDS_ALLOWED"];
            if (!int.TryParse(maxCorpIDsString, out int max) || max <= 0)
            {
                logger.LogWarning(
                    "MAX_CORPIDS_ALLOWED configuration is not set or invalid. Using default value: {DefaultMax}.",
                    DefaultMaxCorpIDsAllowed);
                return DefaultMaxCorpIDsAllowed;
            }

            return max;
        }
        private async Task GetTags()
        {
            Guid c = Guid.NewGuid();
            tagsLoading = true;
            corpIDCounts.Clear();
            unsyncedCounts.Clear();
            userMessage = string.Empty;
            try
            {
                TotalTags = await deviceTagDBService.GetDeviceTagCountAsync(groups, activeTagSearchName);
                TotalPages = (int)Math.Ceiling((decimal)TotalTags / PageSize);

                deviceTags = await deviceTagDBService.GetDeviceTagsByPageAsync(
                    groups,
                    PageNumber,
                    PageSize,
                    activeTagSearchName);
            }
            catch (Exception ex)
            {
                userMessage = $"Error: retrieving tags.\nCorrelation Id: {c.ToString()}";
                logger.LogError($"{userMessage}\n{ex.Message}\nUser: {userName} {userId}");
            }
            finally
            {
                tagsLoading = false;
                await GetSyncedDevicesCounts();
            }
        }
        private async Task GetSyncedDevicesCounts()
        {
            foreach (var deviceTag in deviceTags)
            {
                try
                {
                    corpIDCounts[deviceTag.Id] = 0;
                    unsyncedCounts[deviceTag.Id] = 0;

                    List<Device> devices = await deviceDBService.GetDevicesByTagAsync(deviceTag.Id.ToString());
                    foreach (var device in devices)
                    {
                        if (device.Status == DelegationStationShared.Enums.DeviceStatus.Synced)
                        {
                            corpIDCounts[deviceTag.Id]++;
                        }
                        else
                        {
                            unsyncedCounts[deviceTag.Id]++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Isolate per-tag failures so one bad tag doesn't leave the remaining tags stuck loading.
                    // -1 signals the UI to show "Unable to retrieve device count".
                    corpIDCounts[deviceTag.Id] = -1;
                    unsyncedCounts[deviceTag.Id] = -1;
                    logger.LogError(ex, "Error retrieving synced devices count for tag {TagId}.", deviceTag.Id);
                    userMessage = "Error: retrieving synced devices counts.";
                }
            }
        }
        // Percentage of the max allowed corpIDs used by a single tag's synced device count.
        private string GetTagPercentageDisplay(Guid tagId)
        {
            if (!corpIDCounts.ContainsKey(tagId))
            {
                return "Loading...";
            }

            int count = corpIDCounts[tagId];
            if (count < 0)
            {
                return "N/A";
            }

            if (MaxCorpIDsAllowed <= 0)
            {
                return "0%";
            }

            double percentage = (double)count / MaxCorpIDsAllowed * 100;
            return $"{percentage.ToString("0.##")}%";
        }

        private string GetTagShareOfInUseDisplay(Guid tagId)
        {
            if (!corpIDCounts.ContainsKey(tagId))
            {
                return "Loading...";
            }

            int count = corpIDCounts[tagId];
            if (count < 0)
            {
                return "N/A";
            }

            if (CorpIDCount <= 0)
            {
                return "0%";
            }

            double percentage = (double)count / CorpIDCount * 100;
            return $"{percentage.ToString("0.##")}%";
        }

        private void UpdateClaims()
        {
            groups = new List<string>();

            var roleClaims = user.Claims.Where(c => c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role" || c.Type == "roles");
            roleClaims = roleClaims ?? new List<System.Security.Claims.Claim>();
            foreach (var c in roleClaims)
            {
                groups.Add(c.Value);
            }
            userId = user.Claims.Where(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier").Select(c => c.Value.ToString()).FirstOrDefault() ?? "";
        }
        private async Task NextPage()
        {
            if (PageNumber < TotalPages)
            {
                PageNumber++;
            }
            await GetTags();
        }

        private async Task SearchTags()
        {
            activeTagSearchName = tagSearchName.Trim();
            PageNumber = 1;
            await GetTags();
        }

        private async Task ClearTagSearch()
        {
            tagSearchName = string.Empty;
            activeTagSearchName = string.Empty;
            PageNumber = 1;
            await GetTags();
        }

        private async Task PreviousPage()
        {
            if (PageNumber > 1)
            {
                PageNumber--;
            }
            await GetTags();
        }

        private async Task FirstPage()
        {
            PageNumber = 1;
            await GetTags();
        }

        private async Task LastPage()
        {
            PageNumber = TotalPages;
            await GetTags();
        }
    }
}
