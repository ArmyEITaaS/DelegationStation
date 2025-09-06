using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace DelegationStation.Authorization
{

    public class DeviceTagAuthorizationHandler :
        AuthorizationHandler<OperationAuthorizationRequirement, DeviceTag>
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<DeviceTagAuthorizationHandler> _logger;

        public DeviceTagAuthorizationHandler(IConfiguration configuration, ILogger<DeviceTagAuthorizationHandler> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
                                                       OperationAuthorizationRequirement requirement,
                                                       DeviceTag resource)
        {
            // Get the Default Admin Group
            string defaultAdminGroup = _configuration.GetSection("DefaultAdminGroupObjectId").Value ?? "";
            if (string.IsNullOrEmpty(defaultAdminGroup))
            {
                _logger.LogError("Device Tag Authorization Handler DefaultAdminGroupObjectId not found in configuration");
                context.Fail(new AuthorizationFailureReason(this, "Unable to find default admin group"));
                return Task.CompletedTask;
            }

            // Get the groups from the user
            List<string> groups = new List<string>();
            var roleClaims = context.User.Claims.Where(c => c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role" || c.Type == "roles");
            roleClaims = roleClaims ?? new List<System.Security.Claims.Claim>();
            foreach (var c in roleClaims)
            {
                groups.Add(c.Value);
            }

            // Check if the user is in the default admin group and if so, succeed
            // Covers Create, Update, and Delete implicitely 
            foreach (string group in groups)
            {
                if (group == defaultAdminGroup)
                {
                    context.Succeed(requirement);
                    return Task.CompletedTask;
                }
            }

            if (requirement.Name == DeviceTagOperations.Read.Name || requirement.Name == DeviceTagOperations.BulkUpload.Name)
            {
                foreach (RoleDelegation roleDelegation in resource.RoleDelegations)
                {
                    foreach (string group in groups)
                    {
                        if (group == roleDelegation.SecurityGroupId)
                        {
                            context.Succeed(requirement);
                            return Task.CompletedTask;
                        }
                    }
                }
            }

            if (requirement.Name == DeviceTagOperations.UpdateActions.Name)
            {
                foreach (RoleDelegation roleDelegation in resource.RoleDelegations)
                {
                    foreach (string group in groups)
                    {
                        if (group == roleDelegation.SecurityGroupId)
                        {
                            if (roleDelegation.Role.AdministrativeUnits |
                                roleDelegation.Role.SecurityGroups |
                                (roleDelegation.Role.Attributes.Count > 0 &&
                                    roleDelegation.Role.Attributes.Contains(AllowedAttributes.None) == false))
                            {
                                context.Succeed(requirement);
                                return Task.CompletedTask;
                            }
                        }
                    }
                }
            }

            if (requirement.Name == DeviceTagOperations.UpdateAdministrativeUnits.Name)
            {
                foreach (RoleDelegation roleDelegation in resource.RoleDelegations)
                {
                    foreach (string group in groups)
                    {
                        if (group == roleDelegation.SecurityGroupId)
                        {
                            if (roleDelegation.Role.AdministrativeUnits)
                            {
                                context.Succeed(requirement);
                                return Task.CompletedTask;
                            }
                        }
                    }
                }
            }

            if (requirement.Name == DeviceTagOperations.UpdateSecurityGroups.Name)
            {
                foreach (RoleDelegation roleDelegation in resource.RoleDelegations)
                {
                    foreach (string group in groups)
                    {
                        if (group == roleDelegation.SecurityGroupId)
                        {
                            if (roleDelegation.Role.SecurityGroups)
                            {
                                context.Succeed(requirement);
                                return Task.CompletedTask;
                            }
                        }
                    }
                }
            }

            if (requirement.Name == DeviceTagOperations.UpdateAllAttributes.Name || requirement.Name == DeviceTagOperations.UpdateAttributes.Name)
            {
                foreach (RoleDelegation roleDelegation in resource.RoleDelegations)
                {
                    foreach (string group in groups)
                    {
                        if (group == roleDelegation.SecurityGroupId)
                        {

                            if (roleDelegation.Role.Attributes != null)
                            {
                                if (roleDelegation.Role.Attributes.Where(a => a == AllowedAttributes.All).Count() > 0)
                                {
                                    context.Succeed(requirement);
                                    return Task.CompletedTask;
                                }
                            }
                        }
                    }
                }
            }

            if (requirement.Name == DeviceTagOperations.UpdateExtensionAttribute1.Name || requirement.Name == DeviceTagOperations.UpdateAttributes.Name)
            {
                foreach (RoleDelegation roleDelegation in resource.RoleDelegations)
                {
                    foreach (string group in groups)
                    {
                        if (group == roleDelegation.SecurityGroupId)
                        {

                            if (roleDelegation.Role.Attributes != null)
                            {
                                if (roleDelegation.Role.Attributes.Where(a => a == AllowedAttributes.ExtensionAttribute1).Count() > 0)
                                {
                                    context.Succeed(requirement);
                                    return Task.CompletedTask;
                                }
                            }
                        }
                    }
                }
            }

            if (requirement.Name == DeviceTagOperations.UpdateExtensionAttribute2.Name || requirement.Name == DeviceTagOperations.UpdateAttributes.Name)
            {
                foreach (RoleDelegation roleDelegation in resource.RoleDelegations)
                {
                    foreach (string group in groups)
                    {
                        if (group == roleDelegation.SecurityGroupId)
                        {

                            if (roleDelegation.Role.Attributes != null)
                            {
                                if (roleDelegation.Role.Attributes.Where(a => a == AllowedAttributes.ExtensionAttribute2).Count() > 0)
                                {
                                    context.Succeed(requirement);
                                    return Task.CompletedTask;
                                }
                            }
                        }
                    }
                }
            }

            if (requirement.Name == DeviceTagOperations.UpdateExtensionAttribute3.Name || requirement.Name == DeviceTagOperations.UpdateAttributes.Name)
            {
                foreach (RoleDelegation roleDelegation in resource.RoleDelegations)
                {
                    foreach (string group in groups)
                    {
                        if (group == roleDelegation.SecurityGroupId)
                        {

                            if (roleDelegation.Role.Attributes != null)
                            {
                                if (roleDelegation.Role.Attributes.Where(a => a == AllowedAttributes.ExtensionAttribute3).Count() > 0)
                                {
                                    context.Succeed(requirement);
                                    return Task.CompletedTask;
                                }
                            }
                        }
                    }
                }
            }

            if (requirement.Name == DeviceTagOperations.UpdateExtensionAttribute4.Name || requirement.Name == DeviceTagOperations.UpdateAttributes.Name)
            {
                foreach (RoleDelegation roleDelegation in resource.RoleDelegations)
                {
                    foreach (string group in groups)
                    {
                        if (group == roleDelegation.SecurityGroupId)
                        {

                            if (roleDelegation.Role.Attributes != null)
                            {
                                if (roleDelegation.Role.Attributes.Where(a => a == AllowedAttributes.ExtensionAttribute4).Count() > 0)
                                {
                                    context.Succeed(requirement);
                                    return Task.CompletedTask;
                                }
                            }
                        }
                    }
                }
            }

            if (requirement.Name == DeviceTagOperations.UpdateExtensionAttribute5.Name || requirement.Name == DeviceTagOperations.UpdateAttributes.Name)
            {
                foreach (RoleDelegation roleDelegation in resource.RoleDelegations)
                {
                    foreach (string group in groups)
                    {
                        if (group == roleDelegation.SecurityGroupId)
                        {

                            if (roleDelegation.Role.Attributes != null)
                            {
                                if (roleDelegation.Role.Attributes.Where(a => a == AllowedAttributes.ExtensionAttribute5).Count() > 0)
                                {
                                    context.Succeed(requirement);
                                    return Task.CompletedTask;
                                }
                            }
                        }
                    }
                }
            }

            if (requirement.Name == DeviceTagOperations.UpdateExtensionAttribute6.Name || requirement.Name == DeviceTagOperations.UpdateAttributes.Name)
            {
                foreach (RoleDelegation roleDelegation in resource.RoleDelegations)
                {
                    foreach (string group in groups)
                    {
                        if (group == roleDelegation.SecurityGroupId)
                        {

                            if (roleDelegation.Role.Attributes != null)
                            {
                                if (roleDelegation.Role.Attributes.Where(a => a == AllowedAttributes.ExtensionAttribute6).Count() > 0)
                                {
                                    context.Succeed(requirement);
                                    return Task.CompletedTask;
                                }
                            }
                        }
                    }
                }
            }

            if (requirement.Name == DeviceTagOperations.UpdateExtensionAttribute7.Name || requirement.Name == DeviceTagOperations.UpdateAttributes.Name)
            {
                foreach (RoleDelegation roleDelegation in resource.RoleDelegations)
                {
                    foreach (string group in groups)
                    {
                        if (group == roleDelegation.SecurityGroupId)
                        {

                            if (roleDelegation.Role.Attributes != null)
                            {
                                if (roleDelegation.Role.Attributes.Where(a => a == AllowedAttributes.ExtensionAttribute7).Count() > 0)
                                {
                                    context.Succeed(requirement);
                                    return Task.CompletedTask;
                                }
                            }
                        }
                    }
                }
            }

            if (requirement.Name == DeviceTagOperations.UpdateExtensionAttribute8.Name || requirement.Name == DeviceTagOperations.UpdateAttributes.Name)
            {
                foreach (RoleDelegation roleDelegation in resource.RoleDelegations)
                {
                    foreach (string group in groups)
                    {
                        if (group == roleDelegation.SecurityGroupId)
                        {

                            if (roleDelegation.Role.Attributes != null)
                            {
                                if (roleDelegation.Role.Attributes.Where(a => a == AllowedAttributes.ExtensionAttribute8).Count() > 0)
                                {
                                    context.Succeed(requirement);
                                    return Task.CompletedTask;
                                }
                            }
                        }
                    }
                }
            }

            if (requirement.Name == DeviceTagOperations.UpdateExtensionAttribute9.Name || requirement.Name == DeviceTagOperations.UpdateAttributes.Name)
            {
                foreach (RoleDelegation roleDelegation in resource.RoleDelegations)
                {
                    foreach (string group in groups)
                    {
                        if (group == roleDelegation.SecurityGroupId)
                        {

                            if (roleDelegation.Role.Attributes != null)
                            {
                                if (roleDelegation.Role.Attributes.Where(a => a == AllowedAttributes.ExtensionAttribute9).Count() > 0)
                                {
                                    context.Succeed(requirement);
                                    return Task.CompletedTask;
                                }
                            }
                        }
                    }
                }
            }

            if (requirement.Name == DeviceTagOperations.UpdateExtensionAttribute10.Name || requirement.Name == DeviceTagOperations.UpdateAttributes.Name)
            {
                foreach (RoleDelegation roleDelegation in resource.RoleDelegations)
                {
                    foreach (string group in groups)
                    {
                        if (group == roleDelegation.SecurityGroupId)
                        {

                            if (roleDelegation.Role.Attributes != null)
                            {
                                if (roleDelegation.Role.Attributes.Where(a => a == AllowedAttributes.ExtensionAttribute10).Count() > 0)
                                {
                                    context.Succeed(requirement);
                                    return Task.CompletedTask;
                                }
                            }
                        }
                    }
                }
            }

            if (requirement.Name == DeviceTagOperations.UpdateExtensionAttribute11.Name || requirement.Name == DeviceTagOperations.UpdateAttributes.Name)
            {
                foreach (RoleDelegation roleDelegation in resource.RoleDelegations)
                {
                    foreach (string group in groups)
                    {
                        if (group == roleDelegation.SecurityGroupId)
                        {

                            if (roleDelegation.Role.Attributes != null)
                            {
                                if (roleDelegation.Role.Attributes.Where(a => a == AllowedAttributes.ExtensionAttribute11).Count() > 0)
                                {
                                    context.Succeed(requirement);
                                    return Task.CompletedTask;
                                }
                            }
                        }
                    }
                }
            }

            if (requirement.Name == DeviceTagOperations.UpdateExtensionAttribute12.Name || requirement.Name == DeviceTagOperations.UpdateAttributes.Name)
            {
                foreach (RoleDelegation roleDelegation in resource.RoleDelegations)
                {
                    foreach (string group in groups)
                    {
                        if (group == roleDelegation.SecurityGroupId)
                        {

                            if (roleDelegation.Role.Attributes != null)
                            {
                                if (roleDelegation.Role.Attributes.Where(a => a == AllowedAttributes.ExtensionAttribute12).Count() > 0)
                                {
                                    context.Succeed(requirement);
                                    return Task.CompletedTask;
                                }
                            }
                        }
                    }
                }
            }

            if (requirement.Name == DeviceTagOperations.UpdateExtensionAttribute13.Name || requirement.Name == DeviceTagOperations.UpdateAttributes.Name)
            {
                foreach (RoleDelegation roleDelegation in resource.RoleDelegations)
                {
                    foreach (string group in groups)
                    {
                        if (group == roleDelegation.SecurityGroupId)
                        {

                            if (roleDelegation.Role.Attributes != null)
                            {
                                if (roleDelegation.Role.Attributes.Where(a => a == AllowedAttributes.ExtensionAttribute13).Count() > 0)
                                {
                                    context.Succeed(requirement);
                                    return Task.CompletedTask;
                                }
                            }
                        }
                    }
                }
            }

            if (requirement.Name == DeviceTagOperations.UpdateExtensionAttribute14.Name || requirement.Name == DeviceTagOperations.UpdateAttributes.Name)
            {
                foreach (RoleDelegation roleDelegation in resource.RoleDelegations)
                {
                    foreach (string group in groups)
                    {
                        if (group == roleDelegation.SecurityGroupId)
                        {

                            if (roleDelegation.Role.Attributes != null)
                            {
                                if (roleDelegation.Role.Attributes.Where(a => a == AllowedAttributes.ExtensionAttribute14).Count() > 0)
                                {
                                    context.Succeed(requirement);
                                    return Task.CompletedTask;
                                }
                            }
                        }
                    }
                }
            }

            if (requirement.Name == DeviceTagOperations.UpdateExtensionAttribute15.Name || requirement.Name == DeviceTagOperations.UpdateAttributes.Name)
            {
                foreach (RoleDelegation roleDelegation in resource.RoleDelegations)
                {
                    foreach (string group in groups)
                    {
                        if (group == roleDelegation.SecurityGroupId)
                        {

                            if (roleDelegation.Role.Attributes != null)
                            {
                                if (roleDelegation.Role.Attributes.Where(a => a == AllowedAttributes.ExtensionAttribute15).Count() > 0)
                                {
                                    context.Succeed(requirement);
                                    return Task.CompletedTask;
                                }
                            }
                        }
                    }
                }
            }

            context.Fail(new AuthorizationFailureReason(this, $"{requirement.Name} not delegated to Tag {resource.Name} with Id {resource.Id}"));
            return Task.CompletedTask;
        }
    }

    public static class DeviceTagOperations
    {
        public static OperationAuthorizationRequirement Create =
            new OperationAuthorizationRequirement { Name = nameof(Create) };
        public static OperationAuthorizationRequirement Read =
            new OperationAuthorizationRequirement { Name = nameof(Read) };
        public static OperationAuthorizationRequirement Update =
            new OperationAuthorizationRequirement { Name = nameof(Update) };
        public static OperationAuthorizationRequirement UpdateActions =
            new OperationAuthorizationRequirement { Name = nameof(UpdateActions) };
        public static OperationAuthorizationRequirement Delete =
            new OperationAuthorizationRequirement { Name = nameof(Delete) };
        public static OperationAuthorizationRequirement BulkUpload =
            new OperationAuthorizationRequirement { Name = nameof(BulkUpload) };
        public static OperationAuthorizationRequirement UpdateAdministrativeUnits =
            new OperationAuthorizationRequirement { Name = nameof(UpdateAdministrativeUnits) };
        public static OperationAuthorizationRequirement UpdateSecurityGroups =
            new OperationAuthorizationRequirement { Name = nameof(UpdateSecurityGroups) };
        public static OperationAuthorizationRequirement UpdateAttributes =
            new OperationAuthorizationRequirement { Name = nameof(UpdateAttributes) };
        public static OperationAuthorizationRequirement UpdateAllAttributes =
            new OperationAuthorizationRequirement { Name = nameof(UpdateAllAttributes) };
        public static OperationAuthorizationRequirement UpdateExtensionAttribute1 =
            new OperationAuthorizationRequirement { Name = nameof(UpdateExtensionAttribute1) };
        public static OperationAuthorizationRequirement UpdateExtensionAttribute2 =
            new OperationAuthorizationRequirement { Name = nameof(UpdateExtensionAttribute2) };
        public static OperationAuthorizationRequirement UpdateExtensionAttribute3 =
            new OperationAuthorizationRequirement { Name = nameof(UpdateExtensionAttribute3) };
        public static OperationAuthorizationRequirement UpdateExtensionAttribute4 =
            new OperationAuthorizationRequirement { Name = nameof(UpdateExtensionAttribute4) };
        public static OperationAuthorizationRequirement UpdateExtensionAttribute5 =
            new OperationAuthorizationRequirement { Name = nameof(UpdateExtensionAttribute5) };
        public static OperationAuthorizationRequirement UpdateExtensionAttribute6 =
            new OperationAuthorizationRequirement { Name = nameof(UpdateExtensionAttribute6) };
        public static OperationAuthorizationRequirement UpdateExtensionAttribute7 =
            new OperationAuthorizationRequirement { Name = nameof(UpdateExtensionAttribute7) };
        public static OperationAuthorizationRequirement UpdateExtensionAttribute8 =
            new OperationAuthorizationRequirement { Name = nameof(UpdateExtensionAttribute8) };
        public static OperationAuthorizationRequirement UpdateExtensionAttribute9 =
            new OperationAuthorizationRequirement { Name = nameof(UpdateExtensionAttribute9) };
        public static OperationAuthorizationRequirement UpdateExtensionAttribute10 =
            new OperationAuthorizationRequirement { Name = nameof(UpdateExtensionAttribute10) };
        public static OperationAuthorizationRequirement UpdateExtensionAttribute11 =
            new OperationAuthorizationRequirement { Name = nameof(UpdateExtensionAttribute11) };
        public static OperationAuthorizationRequirement UpdateExtensionAttribute12 =
            new OperationAuthorizationRequirement { Name = nameof(UpdateExtensionAttribute12) };
        public static OperationAuthorizationRequirement UpdateExtensionAttribute13 =
            new OperationAuthorizationRequirement { Name = nameof(UpdateExtensionAttribute13) };
        public static OperationAuthorizationRequirement UpdateExtensionAttribute14 =
            new OperationAuthorizationRequirement { Name = nameof(UpdateExtensionAttribute14) };
        public static OperationAuthorizationRequirement UpdateExtensionAttribute15 =
            new OperationAuthorizationRequirement { Name = nameof(UpdateExtensionAttribute15) };
    }
}