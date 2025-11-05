using DelegationStationShared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace DelegationStation.Pages
{
    public partial class Tags
    {
        [CascadingParameter]
        public Task<AuthenticationState>? AuthState { get; set; }
        private System.Security.Claims.ClaimsPrincipal user = new System.Security.Claims.ClaimsPrincipal();
        private string userId = string.Empty;
        private string userName = string.Empty;

        private List<string> groups = new List<string>();
        private List<DeviceTag> deviceTags = new List<DeviceTag>();
        private DeviceTag newTag = new DeviceTag()
        {
            // default to set for new tags only
            CorpIDSyncEnabled = true
        };
        private string userMessage = string.Empty;
        private bool tagsLoading = true;
        private int TotalTags = 0;
        private int TotalPages = 0;
        private int PageSize = 10;
        private DeviceTag searchTag = new DeviceTag();

        [Parameter] public int PageNumber { get; set; }

        protected override async Task OnInitializedAsync()
        {
            if (AuthState is not null)
            {
                var authState = await AuthState;
                user = authState?.User ?? new System.Security.Claims.ClaimsPrincipal();
                userName = user.Claims.Where(c => c.Type == "name").Select(c => c.Value.ToString()).FirstOrDefault() ?? "";
                userId = user.Claims.Where(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier").Select(c => c.Value.ToString()).FirstOrDefault() ?? "";
            }
            
            PageSize = deviceTagDBService.CurrentSearch.pageSize;
            PageNumber = deviceTagDBService.CurrentSearch.pageNumber;
            searchTag.Name = deviceTagDBService.CurrentSearch.name ?? string.Empty;
            UpdateClaims();
            await GetTags();
        }

        protected override async Task OnParametersSetAsync()
        {
            if (PageNumber < 1 || PageNumber > TotalPages)
            {
                PageNumber = 1;
            }
            deviceTags = await deviceTagDBService.GetDeviceTagsByPageAsync(groups, PageNumber, PageSize, searchTag.Name);
        }
        private async Task GetTagsSearch()
        {
            Guid c = Guid.NewGuid();
            userMessage = string.Empty;
            try
            {
                await FirstPage();
            }
            catch (Exception ex)
            {
                userMessage = $"Error retrieving searching Devices.\nCorrelation Id: {c.ToString()}";
                logger.LogError($"{userMessage}\n{ex.Message}\nUser: {userName} {userId}");
            }
        }

        private async Task NextPage()
        {
            if (PageNumber < TotalPages)
            {
                PageNumber++;
            }
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

        private async Task GetTags()
        {
            Guid c = Guid.NewGuid();
            userMessage = "";
            try
            {
                TotalTags = await deviceTagDBService.GetDeviceTagCountAsync(groups, searchTag.Name);
                TotalPages = (int)Math.Ceiling((decimal)TotalTags / PageSize);

                deviceTags = await deviceTagDBService.GetDeviceTagsByPageAsync(groups, PageNumber, PageSize, searchTag.Name);
            }
            catch (Exception ex)
            {
                userMessage = $"Error: retrieving tags.\nCorrelation Id: {c.ToString()}";
                logger.LogError($"{userMessage}\n{ex.Message}\nUser: {userName} {userId}");
            }
            finally
            {
                tagsLoading = false;
            }
        }

        private async Task AddTag()
        {
            Guid c = Guid.NewGuid();
            if (authorizationService.AuthorizeAsync(user, "DelegationStationAdmin").Result.Succeeded == false)
            {
                userMessage = $"Error: Not authorized to add tags.\nCorrelation Id: {c.ToString()}";
                logger.LogError($"{userMessage}\nUser: {userName} {userId}");
                return;
            }

            userMessage = "";
            if (string.IsNullOrEmpty(newTag.Name))
            {
                userMessage = $"Tag name is required.\nCorrelation Id: {c.ToString()}";
                return;
            }

            try
            {
                DeviceTag resp = await deviceTagDBService.AddOrUpdateDeviceTagAsync(newTag);
                newTag = new DeviceTag();
                newTag.CorpIDSyncEnabled = true;

                userMessage = "Tag added successfully.";
            }
            catch (Exception ex)
            {

                userMessage = $"Error adding tag.\nCorrelation Id: {c.ToString()}";
                logger.LogError($"{userMessage}\n{ex.Message}\nUser: {userName} {userId}");
                return;
            }
            await GetTags();
            await LastPage();

        }
    }
}