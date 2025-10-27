using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace DelegationStation.Pages
{
    public partial class RoleEdit
    {
        [Parameter]
        public string? Id { get; set; }
        [CascadingParameter]
        public Task<AuthenticationState>? AuthState { get; set; }
        private System.Security.Claims.ClaimsPrincipal user = new System.Security.Claims.ClaimsPrincipal();
        private string userId = string.Empty;
        private string userName = string.Empty;

        public Role role { get; set; } = new Role();
        // private string attributeToAdd = "";
        private string userMessage = "";

        protected override async Task OnInitializedAsync()
        {
            if (AuthState is not null)
            {
                var authState = await AuthState;
                user = authState?.User ?? new System.Security.Claims.ClaimsPrincipal();
                userName = user.Claims.Where(c => c.Type == "name").Select(c => c.Value.ToString()).FirstOrDefault() ?? "";
                userId = user.Claims.Where(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier").Select(c => c.Value.ToString()).FirstOrDefault() ?? "";
            }

            Guid g = Guid.NewGuid();

            if (Id == null)
            {
                return;
            }

            if (Id == Guid.Empty.ToString())
            {
                return;
            }

            try
            {
                role = await roleDBService.GetRoleAsync(Id);
            }
            catch (Exception e)
            {
                var erMessage = $"Correlation Id: {g.ToString()}\nError getting role by id";
                logger.LogError(e, $"{erMessage}\nUser: {userName} {userId}");
                Console.WriteLine(erMessage);
                userMessage = erMessage;
            }
        }

        private async Task SaveRole()
        {
            Guid g = Guid.NewGuid();

            if (authorizationService.AuthorizeAsync(user, "DelegationStationAdmin").Result.Succeeded == false)
            {
                var message = $"Correlation Id: {g.ToString()}\nUser is not authorized to save roles.";
                logger.LogInformation($"{message}\nUser: {userName} {userId}");
                nav.NavigateTo("/Roles");
                return;
            }

            try
            {
                role.Attributes.Where(a => a == AllowedAttributes.All).ToList().ForEach(a => role.Attributes.Remove(a));
                role = await roleDBService.AddOrUpdateRoleAsync(role);
                var message = $"Correlation Id: {g.ToString()}\nSaved role.";
                logger.LogInformation($"{message}\nUser: {userName} {userId}");
                nav.NavigateTo("/Roles");
            }
            catch (Exception e)
            {
                var erMessage = $"Correlation Id: {g.ToString()}\nError getting role by id";
                logger.LogError(e, $"{erMessage}\nUser: {userName} {userId}");
                userMessage = erMessage;
            }
        }

        private void AttributesChanged(AllowedAttributes attr, ChangeEventArgs e)
        {
            var value = e?.Value?.ToString();

            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            if (Convert.ToBoolean(value) && !role.Attributes.Contains(attr))
            {
                role.Attributes.Add(attr);
            }
            else
            {
                role.Attributes.Remove(attr);
            }
        }
    }
}