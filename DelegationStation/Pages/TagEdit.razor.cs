using DelegationStation.Shared;
using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Graph.Models;


namespace DelegationStation.Pages
{
    public partial class TagEdit
    {
        [CascadingParameter]
        public Task<AuthenticationState>? AuthState { get; set; }
        private System.Security.Claims.ClaimsPrincipal user = new System.Security.Claims.ClaimsPrincipal();
        private string userId = string.Empty;
        private string userName = string.Empty;

        [Parameter] public string? Id { get; set; }

        private ConfirmMessage? ConfirmDelete;
        private ConfirmMessage? ConfirmSave;

        private DeviceTag _tag = new DeviceTag();

        private List<string> groups = new List<string>();
        private List<Role> roles = new List<Role>();

        private DeviceTag tag = new DeviceTag();
        private RoleDelegation roleDelegation = new RoleDelegation();
        private DeviceUpdateAction deviceUpdateAction = new DeviceUpdateAction();
        private string defaultGroup = "";
        private string deviceUpdateActionsValidationMessage = "";
        private string tagErrorMessage = "";
        private string tagSuccessMessage = "";
        private EditContext? deviceUpdateActionEditContext;
        private string userMessage = "";

        private string securityGroupSearchString = "";
        private List<Group> securityGroupSearchResults = new List<Group>();
        private string securityGroupSearchMessage = "";
        private bool securityGroupSearchInProgress = false;
        private bool securityGroupSearchExecuted = false;

        private string administrativeUnitSearchString = "";
        private List<AdministrativeUnit> administrativeUnitSearchResults = new List<AdministrativeUnit>();
        private string administrativeUnitSearchMessage = "";
        private bool administrativeUnitSearchInProgress = false;
        private bool administrativeUnitSearchExecuted = false;

        private string roleSecurityGroupSearchString = "";
        private List<Group> roleSecurityGroupSearchResults = new List<Group>();
        private string roleSecurityGroupSearchMessage = "";
        private bool roleSecurityGroupSearchInProgress = false;
        private bool roleSecurityGroupSearchExecuted = false;
        private string addRoleMessage = "";

        private string testEnrollmentUser = "";
        private bool? testEnrollmentUserResult = false;
        private bool displayTestResult = false;

        private int deviceCount = 0;


        protected override async Task OnInitializedAsync()
        {
            deviceUpdateActionEditContext = new EditContext(deviceUpdateAction);
            deviceUpdateActionEditContext.OnFieldChanged += DeviceUpdateAction_OnFieldChanged;

            defaultGroup = config.GetSection("DefaultAdminGroupObjectId").Value ?? "";
            if (AuthState is not null)
            {
                var authState = await AuthState;
                user = authState?.User ?? new System.Security.Claims.ClaimsPrincipal();
                userName = user.Claims.Where(c => c.Type == "name").Select(c => c.Value.ToString()).FirstOrDefault() ?? "";
                userId = user.Claims.Where(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier").Select(c => c.Value.ToString()).FirstOrDefault() ?? "";
            }

            await GetTag();

            if (_tag != null)
            {
                await UpdateRoleDelegationGroups();
                await GetRolesAsync();
            }
            await SetInitialUpdateAction();
            deviceCount = await GetDeviceCount();
        }

        private async Task<int> GetDeviceCount()
        {
            try
            {
                return await deviceTagDBService.GetDeviceCountByTagIdAsync(tag.Id.ToString());
            }
            catch (Exception ex)
            {
                logger.LogError($"Failed to get Device Count.\n{ex.Message}");
                return -1;
            }
        }

        private async Task GetRolesAsync()
        {
            Guid c = Guid.NewGuid();
            userMessage = string.Empty;

            try
            {
                roles = await roleDBService.GetRolesAsync();
            }
            catch (Exception ex)
            {
                var message = $"Failed to get Roles.\nCorrelation Id: {c.ToString()}";
                userMessage = message;
                logger.LogError($"{message}.\n{ex.Message}\nUser: {userName} {userId}");
            }
        }


        private void DeviceUpdateAction_OnFieldChanged(object? sender, FieldChangedEventArgs e)
        {
            deviceUpdateActionEditContext?.Validate();
            if (e.FieldIdentifier.FieldName == nameof(deviceUpdateAction.ActionType))
            {
                DeviceUpdateActionTypeChanged(deviceUpdateAction.ActionType.ToString());
            }
        }

        private void DeviceUpdateActionTypeChanged(string value)
        {
            DeviceUpdateActionType a = deviceUpdateAction.ActionType;
            deviceUpdateAction = new DeviceUpdateAction();
            deviceUpdateAction.ActionType = a;
        }

        private async Task GetTag()
        {
            Guid c = Guid.NewGuid();
            userMessage = string.Empty;

            try
            {
                if (Id != null)
                {
                    _tag = await deviceTagDBService.GetDeviceTagAsync(Id);

                    if (_tag != null)
                    {
                        tag = _tag.DeepCopyKeepId();
                    }
                    else
                    {
                        var message = $"Failed to find Device Tag {Id} to Edit.\nCorrelation Id: {c.ToString()}";
                        tagErrorMessage = message;
                        logger.LogError($"{message}\nUser: {userName} {userId}");
                        return;
                    }

                    if (!authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.Read).Result.Succeeded)
                    {
                        var message = $"User {userName} {userId} is not authorized to read Device Tag {Id}.\nCorrelation Id: {c.ToString()}";
                        tagErrorMessage = message;
                        logger.LogError(message);
                        return;
                    }
                    foreach (RoleDelegation roleDelegation in _tag.RoleDelegations)
                    {
                        roleDelegation.Role = await roleDBService.GetRoleAsync(roleDelegation.Role.Id.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                var message = $"Failed to find Device Tag {Id} to Edit.\nCorrelation Id: {c.ToString()}";
                tagErrorMessage = message;
                logger.LogError($"{message}.\n{ex.Message}\nUser: {userName} {userId}");
            }
            StateHasChanged();
        }

        private bool ActionAllowed(DeviceUpdateAction action)
        {
            Guid c = Guid.NewGuid();
            userMessage = string.Empty;

            if (authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.Update).Result.Succeeded)
            {
                return true;
            }

            if (action.ActionType == DeviceUpdateActionType.Group)
            {
                return authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateSecurityGroups).Result.Succeeded;
            }

            if (action.ActionType == DeviceUpdateActionType.AdministrativeUnit)
            {
                return authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateAdministrativeUnits).Result.Succeeded;
            }

            if (action.ActionType == DeviceUpdateActionType.Attribute)
            {
                try
                {
                    AllowedAttributes attr;
                    Enum.TryParse(action.Name, out attr);
                    return AttributeAllowed(attr);
                }
                catch (Exception ex)
                {
                    var message = $"Parsing Attribute {action.Name} failed.\nCorrelation Id: {c.ToString()}";
                    userMessage = message;
                    logger.LogWarning($"{message}.\n{ex.Message}\nUser: {userName} {userId}");
                }
            }


            return false;
        }

        private bool AttributeAllowed(AllowedAttributes e)
        {
            switch (e)
            {
                case AllowedAttributes.All:
                    return authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateAllAttributes).Result.Succeeded;
                case AllowedAttributes.ExtensionAttribute1:
                    return authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateExtensionAttribute1).Result.Succeeded;
                case AllowedAttributes.ExtensionAttribute2:
                    return authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateExtensionAttribute2).Result.Succeeded;
                case AllowedAttributes.ExtensionAttribute3:
                    return authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateExtensionAttribute3).Result.Succeeded;
                case AllowedAttributes.ExtensionAttribute4:
                    return authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateExtensionAttribute4).Result.Succeeded;
                case AllowedAttributes.ExtensionAttribute5:
                    return authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateExtensionAttribute5).Result.Succeeded;
                case AllowedAttributes.ExtensionAttribute6:
                    return authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateExtensionAttribute6).Result.Succeeded;
                case AllowedAttributes.ExtensionAttribute7:
                    return authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateExtensionAttribute7).Result.Succeeded;
                case AllowedAttributes.ExtensionAttribute8:
                    return authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateExtensionAttribute8).Result.Succeeded;
                case AllowedAttributes.ExtensionAttribute9:
                    return authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateExtensionAttribute9).Result.Succeeded;
                case AllowedAttributes.ExtensionAttribute10:
                    return authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateExtensionAttribute10).Result.Succeeded;
                case AllowedAttributes.ExtensionAttribute11:
                    return authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateExtensionAttribute11).Result.Succeeded;
                case AllowedAttributes.ExtensionAttribute12:
                    return authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateExtensionAttribute12).Result.Succeeded;
                case AllowedAttributes.ExtensionAttribute13:
                    return authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateExtensionAttribute13).Result.Succeeded;
                case AllowedAttributes.ExtensionAttribute14:
                    return authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateExtensionAttribute14).Result.Succeeded;
                case AllowedAttributes.ExtensionAttribute15:
                    return authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateExtensionAttribute15).Result.Succeeded;
                default:
                    return false;
            }
        }

        private async Task GetRoleDelegationName(EventArgs? e)
        {
            Guid c = Guid.NewGuid();
            userMessage = string.Empty;

            if (string.IsNullOrEmpty(roleDelegation.SecurityGroupId))
            {
                return;
            }

            if (!System.Text.RegularExpressions.Regex.Match(roleDelegation.SecurityGroupId, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success)
            {
                return;
            }

            try
            {
                string name = await graphService.GetSecurityGroupName(roleDelegation.SecurityGroupId);
                roleDelegation.SecurityGroupName = name;
            }
            catch (Exception ex)
            {
                var message = $"Failed to get Security Group Name for {roleDelegation.SecurityGroupId}.\nCorrelation Id: {c.ToString()}";
                deviceUpdateActionsValidationMessage = message;
                logger.LogWarning($"{message}.\n{ex.Message}\nUser: {userName} {userId}");
                return;
            }
        }

        private async Task GetActionSecurityGroupName(EventArgs? e)
        {
            Guid c = Guid.NewGuid();
            userMessage = string.Empty;

            if (string.IsNullOrEmpty(deviceUpdateAction.Value))
            {
                var message = $"Device Update Action Value ({deviceUpdateAction.Value}) is empty.\nCorrelation Id: {c.ToString()}";
                deviceUpdateActionsValidationMessage = message;
                logger.LogWarning($"{message}.\nUser: {userName} {userId}");
                return;
            }

            if (!System.Text.RegularExpressions.Regex.Match(deviceUpdateAction.Value, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success)
            {
                var message = $"Device Update Action Value ({deviceUpdateAction.Value}) is not a valid GUID.\nCorrelation Id: {c.ToString()}";
                deviceUpdateActionsValidationMessage = message;
                logger.LogWarning($"{message}.\nUser: {userName} {userId}");
                return;
            }

            try
            {
                string name = await graphService.GetSecurityGroupName(deviceUpdateAction.Value);
                deviceUpdateAction.Name = name;
            }
            catch (Exception ex)
            {
                var message = $"Failed to get Security Group Name for {deviceUpdateAction.Value}.\nCorrelation Id: {c.ToString()}";
                deviceUpdateActionsValidationMessage = message;
                logger.LogWarning($"{message}\n{ex.Message}\nUser: {userName} {userId}");
                return;
            }
        }

        private void AddRoleDelegation()
        {
            Guid c = Guid.NewGuid();
            userMessage = string.Empty;

            addRoleMessage = "";
            if (authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.Update).Result.Succeeded == false)
            {
                var message = $"Error: '{userName}' {userId} attempted to update Tag '{tag.Name}' {tag.Id} without Admin rights.\nCorrelation Id: {c.ToString()}";
                addRoleMessage = message;
                logger.LogWarning($"{message}\nUser: {userName} {userId}");
                return;
            }

            if (string.IsNullOrEmpty(roleDelegation.SecurityGroupId))
            {
                addRoleMessage = $"Error: Security Group ID is required";
                return;
            }

            if (string.IsNullOrEmpty(roleDelegation.SecurityGroupName))
            {
                addRoleMessage = $"Error: Security Group Name is required";
                return;
            }

            if (roleDelegation.Role.Name == "")
            {
                addRoleMessage = $"Error: Role is required";
                return;
            }

            if (tag.RoleDelegations.Where(r => r.SecurityGroupId == roleDelegation.SecurityGroupId).Count() != 0 && tag.RoleDelegations.Where(r => r.Role.Id == roleDelegation.Role.Id).Count() != 0)
            {
                addRoleMessage = $"Error: Security Group '{roleDelegation.SecurityGroupName}' is already added as '{roleDelegation.Role.Name}'";
                return;
            }

            try
            {
                tag.RoleDelegations.Add(roleDelegation);
                roleDelegation = new RoleDelegation();
            }
            catch (Exception ex)
            {
                var message = $"Failed to get Security Group Name for {roleDelegation.SecurityGroupId}.\nCorrelation Id: {c.ToString()}";
                addRoleMessage = message;
                logger.LogWarning($"{message}\n{ex.Message}\nUser: {userName} {userId}");
                return;
            }
        }

        private void AddDeviceUpdateAction(EditContext editContext)
        {
            deviceUpdateActionsValidationMessage = "";
            deviceUpdateAction.Name = deviceUpdateAction.Name.Trim();
            deviceUpdateAction.Value = deviceUpdateAction.Value.Trim();

            if (deviceUpdateAction.ActionType == DeviceUpdateActionType.Group && !System.Text.RegularExpressions.Regex.Match(deviceUpdateAction.Value, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success)
            {
                deviceUpdateActionsValidationMessage = $"When using a group update action value must be a valid GUID";
                return;
            }

            if (deviceUpdateAction.ActionType == DeviceUpdateActionType.Group)
            {
                if (string.IsNullOrEmpty(deviceUpdateAction.Name))
                {
                    deviceUpdateActionsValidationMessage = $"Failed to find group id {deviceUpdateAction.Value}";
                    return;
                }
            }

            if (deviceUpdateAction.ActionType == DeviceUpdateActionType.AdministrativeUnit)
            {
                if (string.IsNullOrEmpty(deviceUpdateAction.Name))
                {
                    deviceUpdateActionsValidationMessage = $"Administrative Unit Name is required";
                    return;
                }

                if (string.IsNullOrEmpty(deviceUpdateAction.Value))
                {
                    deviceUpdateActionsValidationMessage = $"Administrative Unit Id is required";
                    return;
                }
            }


            deviceUpdateActionsValidationMessage = "";
            if (tag.UpdateActions.Where(u => u.ActionType == deviceUpdateAction.ActionType && u.Name == deviceUpdateAction.Name).Count() > 0)
            {
                deviceUpdateActionsValidationMessage = $"Duplicate update action {deviceUpdateAction.ActionType} {deviceUpdateAction.Value}";
                return;
            }
            tag.UpdateActions.Add(deviceUpdateAction);
            deviceUpdateAction = new DeviceUpdateAction();
        }

        private async Task<bool> ChangesSinceLoad(Guid CorrelationId)
        {
            bool changes = false;
            try
            {
                DeviceTag t = await deviceTagDBService.GetDeviceTagAsync(tag.Id.ToString());
                if (t != null)
                {
                    if (t.Modified != _tag.Modified)
                    {
                        changes = true;
                    }
                }
                tagSuccessMessage = "";
                tagErrorMessage = "";
            }
            catch (Exception ex)
            {
                var message = $"Failed to get Tag {tag.Id} from DB.\nCorrelation Id: {CorrelationId.ToString()}";
                tagSuccessMessage = "";
                tagErrorMessage = message;
                logger.LogWarning($"{message}\n{ex.Message}\nUser: {userName} {userId}");
                return true;
            }
            return changes;
        }

        private static bool IsRegexPatternValid(string pattern)
        {
            try
            {
                new System.Text.RegularExpressions.Regex(pattern);
            }
            catch (ArgumentException)
            {
                return false;
            }
            return true;
        }

        private async Task Save()
        {
            Guid g = Guid.NewGuid();

            if (String.IsNullOrEmpty(tag.AllowedUserPrincipalName) == false && IsRegexPatternValid(tag.AllowedUserPrincipalName) == false)
            {
                tagSuccessMessage = "";
                tagErrorMessage = $"Error: Invalid Allowed User Principal Name Regex Pattern. Correlation Id:{g.ToString()}";
                logger.LogWarning(tagErrorMessage);
                return;
            }

            if (await ChangesSinceLoad(g))
            {
                var message = $"Error: Tag {tag.Id} has been modified since it was loaded. Please refresh the page and try again.\nCorrelation Id:{g.ToString()}";
                tagSuccessMessage = "";
                tagErrorMessage = message;
                logger.LogWarning($"{message}\nUser: {userName} {userId}");
                return;
            }

            // If the user is not authorized
            if (authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateSecurityGroups).Result.Succeeded == false &&
                   authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateAdministrativeUnits).Result.Succeeded == false &&
                   authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateAttributes).Result.Succeeded == false)
            {
                tag = _tag.DeepCopyKeepId();
                var message = $"Error: '{userName}' {userId} is not authorized to save Tag {tag.Name} {tag.Id}.\nCorrelation Id:{g.ToString()}";
                tagSuccessMessage = "";
                tagErrorMessage = message;
                logger.LogWarning($"{message}\nUser: {userName} {userId}");
                return;
            }

            // Update everything if user is an admin
            if (authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.Update).Result.Succeeded)
            {
                try
                {
                    _tag = tag.DeepCopyKeepId();
                    _tag.Modified = DateTime.UtcNow;
                    _tag.ModifiedBy = userId;
                    await deviceTagDBService.AddOrUpdateDeviceTagAsync(_tag);
                    var message = $"Success: '{userName}' {userId} saved Tag '{tag.Name}' {tag.Id}\nCorrelation Id:{g.ToString()}";
                    tagSuccessMessage = message;
                    tagErrorMessage = "";
                    logger.LogInformation($"{message}\nUser: {userName} {userId}");
                }
                catch (Exception ex)
                {
                    var message = $"Error: '{userName}' {userId} unable to save Tag {tag.Id}. Correlation Id:{g.ToString()}";
                    tagSuccessMessage = "";
                    tagErrorMessage = message;
                    logger.LogError($"{message}\n{ex.Message}\nUser: {userName} {userId}");
                }
                tag = _tag.DeepCopyKeepId();
                return;
            }

            // Parse the user role to identify the activities they can perform
            // No updates to _tag delegation
            // User can update Attributes
            if (authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateAttributes).Result.Succeeded)
            {
                foreach (AllowedAttributes attribute in Enum.GetValues(typeof(AllowedAttributes)))
                {
                    if (AttributeAllowed(attribute))
                    {
                        if (tag.UpdateActions.Where(a => a.Name == attribute.ToString()).Count() > 0 && _tag.UpdateActions.Where(a => a.Name == attribute.ToString()).Count() > 0)
                        {
                            _tag.UpdateActions.Where(a => a.Name == attribute.ToString()).First().Value = tag.UpdateActions.Where(a => a.Name == attribute.ToString()).First().Value;
                        }
                        else if (tag.UpdateActions.Where(a => a.Name == attribute.ToString()).Count() > 0)
                        {
                            _tag.UpdateActions.Add(tag.UpdateActions.Where(a => a.Name == attribute.ToString()).First());
                        }
                        else if (tag.UpdateActions.Where(a => a.Name == attribute.ToString()).Count() <= 0)
                        {
                            _tag.UpdateActions.RemoveAll(a => a.Name == attribute.ToString());
                        }
                    }
                }
            }

            // User can update Groups
            if (authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateSecurityGroups).Result.Succeeded)
            {
                List<DeviceUpdateAction> groupActions = tag.UpdateActions.Where(a => a.ActionType == DeviceUpdateActionType.Group).ToList();
                _tag.UpdateActions.RemoveAll(a => a.ActionType == DeviceUpdateActionType.Group);
                _tag.UpdateActions.AddRange(groupActions);

            }

            // User can update Administrative Units
            if (authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.UpdateAdministrativeUnits).Result.Succeeded)
            {
                List<DeviceUpdateAction> administrativeUnitActions = tag.UpdateActions.Where(a => a.ActionType == DeviceUpdateActionType.AdministrativeUnit).ToList();
                _tag.UpdateActions.RemoveAll(a => a.ActionType == DeviceUpdateActionType.AdministrativeUnit);
                _tag.UpdateActions.AddRange(administrativeUnitActions);
            }


            try
            {
                _tag.Modified = DateTime.UtcNow;
                _tag.ModifiedBy = userId;
                _tag = await deviceTagDBService.AddOrUpdateDeviceTagAsync(_tag);
                var message = $"Success: '{userName}' {userId} saved Tag '{tag.Name}' {tag.Id}.\nCorrelation Id:{g.ToString()}";
                tagSuccessMessage = message;
                tagErrorMessage = "";
                logger.LogInformation($"{message}\nUser: {userName} {userId}");
                tag = _tag.DeepCopyKeepId();
            }
            catch (Exception ex)
            {
                var message = $"Error: '{userName}' {userId} unable to save Tag {tag.Id}.\nCorrelation Id:{g.ToString()}";
                tagSuccessMessage = "";
                tagErrorMessage = message;
                logger.LogError($"{message}\n{ex.Message}\nUser: {userName} {userId}");
            }
        }

        private async Task Delete()
        {
            // Error correlation Id
            Guid g = Guid.NewGuid();
            userMessage = string.Empty;

            // Check if there are devices associated with the tag
            deviceCount = await GetDeviceCount();
            if (deviceCount == -1)
            {
                tagSuccessMessage = "";
                tagErrorMessage = $"Error: '{userName}' {userId} unable to check for devices on Tag {tag.Id}.\nCorrelation Id:{g.ToString()}";
                logger.LogWarning($"{tagErrorMessage}");
                StateHasChanged();
                return;
            }

            if (deviceCount > 0)
            {
                var message = $"Error: '{userName}' {userId} unable to check for devices on Tag {tag.Id}.\nCorrelation Id:{g.ToString()}";
                tagSuccessMessage = "";
                tagErrorMessage = $"Error: '{userName}' {userId} attempted to delete Tag '{tag.Name}' {tag.Id} with {deviceCount} devices still associated with it.\nCorrelation Id:{g.ToString()}";
                logger.LogWarning($"{tagErrorMessage}");
                StateHasChanged();
                return;
            }
            StateHasChanged();

            // Check if user is an admin and only then delete the tag
            if (authorizationService.AuthorizeAsync(user, _tag, Authorization.DeviceTagOperations.Delete).Result.Succeeded)
            {
                try
                {
                    await deviceTagDBService.DeleteDeviceTagAsync(tag);
                    nav.NavigateTo("/Tags");
                }
                catch (Exception ex)
                {
                    var message = $"Error: Unable to delete Tag {tag.Id}.\nCorrelation Id:{g.ToString()}";
                    userMessage = message;
                    logger.LogError($"{message}\n{ex.Message}\nUser: {userName} {userId}");
                }
            }
            else
            {
                var message = $"Error: '{userName}' {userId} attempted to delete Tag '{tag.Name}' {tag.Id} without proper permissions.\nCorrelation Id:{g.ToString()}";
                tagSuccessMessage = "";
                tagErrorMessage = message;
                logger.LogWarning($"{message}\nUser: {userName} {userId}");
            }
        }

        private async Task GetGroupForNewUpdateAction(KeyboardEventArgs e)
        {
            Guid c = Guid.NewGuid();
            deviceUpdateAction.Name = "Querying for name";
            StateHasChanged();
            try
            {
                if (deviceUpdateAction.ActionType == DeviceUpdateActionType.Group && System.Text.RegularExpressions.Regex.Match(deviceUpdateAction.Value.Trim(), "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success)
                {
                    deviceUpdateAction.Name = await graphService.GetSecurityGroupName(deviceUpdateAction.Value);
                }
                else
                {
                    deviceUpdateAction.Name = "Not valid security group Id";
                }
                return;
            }
            catch (Exception ex)
            {
                var message = $"Error: Unable to get group name.\nCorrelation Id:{c.ToString()}";
                tagSuccessMessage = "";
                tagErrorMessage = message;
                logger.LogError($"{message}\n{ex.Message}\nUser: {userName} {userId}");
            }
            deviceUpdateAction.Name = "";
        }

        private async Task UpdateRoleDelegationGroups()
        {
            Guid c = Guid.NewGuid();
            userMessage = string.Empty;

            foreach (RoleDelegation r in tag.RoleDelegations)
            {
                try
                {
                    if (System.Text.RegularExpressions.Regex.Match(r.SecurityGroupId, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success)
                    {
                        r.SecurityGroupName = await graphService.GetSecurityGroupName(r.SecurityGroupId);
                    }
                }
                catch (Exception ex)
                {
                    var message = $"Error: Unable to get group {r.SecurityGroupId} for RoleDelegation {r.Id} under Tag {tag.Id}.\nCorrelation Id:{c.ToString()}";
                    userMessage = message;
                    logger.LogError($"{message}\n{ex.Message}\nUser: {userName} {userId}");
                }
            }
        }

        private async Task UpdateUpdateActionGroups()
        {
            Guid c = Guid.NewGuid();
            userMessage = string.Empty;

            foreach (DeviceUpdateAction action in tag.UpdateActions)
            {
                try
                {
                    if (System.Text.RegularExpressions.Regex.Match(action.Value, "^([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})$").Success)
                    {
                        action.Name = await graphService.GetSecurityGroupName(action.Value);
                    }
                }
                catch (Exception ex)
                {
                    var message = $"Error: Unable to get group {action.Value} for UpdateAction {action.Id} under Tag {tag.Id}.\nCorrelation Id:{c.ToString()}";
                    userMessage = message;
                    logger.LogError($"{message}\n{ex.Message}\nUser: {userName} {userId}");
                }
            }
        }

        private void UpdateRole(ChangeEventArgs e)
        {
            var value = ((ChangeEventArgs)e).Value?.ToString();
            if (!string.IsNullOrEmpty(value))
            {
                Role? r = roles.Where(r => r.Id == Guid.Parse(value)).FirstOrDefault();
                if (r != null)
                {
                    roleDelegation.Role = r;
                }
            }
        }

        private void DeviceUpdateActionChanged(ChangeEventArgs e)
        {
            Guid c = Guid.NewGuid();
            userMessage = string.Empty;

            try
            {
                if (e.Value == null)
                {
                    return;
                }
#pragma warning disable CS8604 // Possible null reference argument. Handled in line 880-883 with null check
                deviceUpdateAction.ActionType = (DeviceUpdateActionType)Enum.Parse(typeof(DeviceUpdateActionType), e.Value.ToString());
            }
            catch (Exception ex)
            {
                deviceUpdateAction.ActionType = DeviceUpdateActionType.Attribute;
                var message = $"Error: Unable to parse DeviceUpdateActionType {e.ToString()}.\nCorrelation Id:{c.ToString()}";
                userMessage = message;
                logger.LogError($"{message}\n{ex.Message}\nUser: {userName} {userId}");
            }
            deviceUpdateAction.Name = "";
            deviceUpdateAction.Value = "";
        }

        private void SelectSecurityGroup(Group g)
        {
            deviceUpdateAction.Name = g.DisplayName ?? "";
            deviceUpdateAction.Value = g.Id ?? "";
        }

        private async void SearchSecurityGroups()
        {
            Guid c = Guid.NewGuid();
            userMessage = string.Empty;

            if (securityGroupSearchInProgress)
            {
                return;
            }
            securityGroupSearchInProgress = true;
            securityGroupSearchExecuted = true;
            StateHasChanged();


            if (string.IsNullOrEmpty(securityGroupSearchString))
            {
                securityGroupSearchMessage = "Error: Search string is empty";
                securityGroupSearchInProgress = false;
                return;
            }

            try
            {
                securityGroupSearchResults = await graphService.SearchGroupAsync(securityGroupSearchString);
                securityGroupSearchMessage = "";
            }
            catch (Exception ex)
            {
                var message = $"Error: Unable to search for groups.\nCorrelation Id:{c.ToString()}";
                securityGroupSearchMessage = message;

                logger.LogError($"{message}\n{ex.Message}\nUser: {userName} {userId}");
            }
            finally
            {
                securityGroupSearchInProgress = false;
                StateHasChanged();
            }
        }

        private void SearchSecurityGroupsKeyUp(KeyboardEventArgs e)
        {
            if (e.Key == "Enter")
            {
                SearchSecurityGroups();
            }
        }

        private void SelectAdministrativeUnit(AdministrativeUnit a)
        {
            deviceUpdateAction.Name = a.DisplayName ?? "";
            deviceUpdateAction.Value = a.Id ?? "";
        }

        private async void SearchAdministrativeUnits()
        {
            Guid c = Guid.NewGuid();
            userMessage = string.Empty;

            if (administrativeUnitSearchInProgress)
            {
                return;
            }
            administrativeUnitSearchInProgress = true;
            administrativeUnitSearchExecuted = true;
            StateHasChanged();


            if (string.IsNullOrEmpty(administrativeUnitSearchString))
            {
                administrativeUnitSearchMessage = "Error: Search string is empty";
                administrativeUnitSearchInProgress = false;
                return;
            }

            try
            {
                administrativeUnitSearchResults = await graphService.SearchAdministrativeUnitAsync(administrativeUnitSearchString);
            }
            catch (Exception ex)
            {
                var message = $"Error: Unable to search for groups.\nCorrelation Id:{c.ToString()}";
                administrativeUnitSearchMessage = message;

                logger.LogError($"{message}\n{ex.Message}\nUser: {userName} {userId}");
            }
            finally
            {
                administrativeUnitSearchInProgress = false;
                StateHasChanged();
            }
        }

        private void SearchAdministrativeUnitsKeyUp(KeyboardEventArgs e)
        {
            if (e.Key == "Enter")
            {
                SearchAdministrativeUnits();
            }
        }

        private async void RoleSearchSecurityGroups()
        {
            Guid c = Guid.NewGuid();
            userMessage = string.Empty;

            if (roleSecurityGroupSearchInProgress)
            {
                return;
            }
            roleSecurityGroupSearchInProgress = true;
            roleSecurityGroupSearchExecuted = true;
            StateHasChanged();


            if (string.IsNullOrEmpty(roleSecurityGroupSearchString))
            {
                roleSecurityGroupSearchMessage = "Error: Search string is empty";
                roleSecurityGroupSearchInProgress = false;
                return;
            }

            try
            {
                roleSecurityGroupSearchResults = await graphService.SearchGroupAsync(roleSecurityGroupSearchString);
            }
            catch (Exception ex)
            {
                var message = $"Error: Unable to search for groups.\nCorrelation Id:{c.ToString()}";
                roleSecurityGroupSearchMessage = message;

                logger.LogError($"{message}\n{ex.Message}\nUser: {userName} {userId}");
            }
            finally
            {
                roleSecurityGroupSearchInProgress = false;
                StateHasChanged();
            }
        }

        private void ValidateTestEnrollmentUser()
        {
            if (testEnrollmentUser != null)
            {
                if (IsRegexPatternValid(tag.AllowedUserPrincipalName))
                {
                    testEnrollmentUserResult = System.Text.RegularExpressions.Regex.IsMatch(testEnrollmentUser, tag.AllowedUserPrincipalName);
                }
                else
                {
                    testEnrollmentUserResult = null;
                }
            }

            if (string.IsNullOrEmpty(tag.AllowedUserPrincipalName))
            {
                testEnrollmentUserResult = true;
            }
            displayTestResult = true;
        }

        private void ClearTestEnrollmentResult()
        {
            displayTestResult = false;
        }

        private void RoleSearchSecurityGroupsKeyUp(KeyboardEventArgs e)
        {
            if (e.Key == "Enter")
            {
                RoleSearchSecurityGroups();
            }
        }

        private void RoleSelectSecurityGroup(Group g)
        {
            roleDelegation.SecurityGroupName = g.DisplayName ?? "";
            roleDelegation.SecurityGroupId = g.Id ?? "";
        }

        private void ConfirmDeleteTag()
        {
            ConfirmDelete?.Show();
        }

        private async Task SetInitialUpdateAction()
        {
            if((await authorizationService.AuthorizeAsync(user, _tag, "TagUpdateActionAttributes")).Succeeded)
            {
                deviceUpdateAction.ActionType = DeviceUpdateActionType.Attribute;
            }
            else if((await authorizationService.AuthorizeAsync(user, _tag, "TagUpdateActionSecurityGroups")).Succeeded)
            {
                deviceUpdateAction.ActionType = DeviceUpdateActionType.Group;
            }
            else if((await authorizationService.AuthorizeAsync(user, _tag, "TagUpdateActionAdministrativeUnits")).Succeeded)
            {
                deviceUpdateAction.ActionType = DeviceUpdateActionType.AdministrativeUnit;
            }
        }

        private bool validateActionInputs()
        {
            if(deviceUpdateAction.ActionType == DeviceUpdateActionType.Group)
            {
                return !(string.IsNullOrEmpty(deviceUpdateAction.Name));
            }
            else if (deviceUpdateAction.ActionType == DeviceUpdateActionType.Attribute)
            {
                return !(string.IsNullOrEmpty(deviceUpdateAction.Value) || string.IsNullOrEmpty(deviceUpdateAction.Name));
            }
            else if(deviceUpdateAction.ActionType == DeviceUpdateActionType.AdministrativeUnit)
            {
                return !(string.IsNullOrEmpty(deviceUpdateAction.Name));
            }

            return false;
        }

    }
}
