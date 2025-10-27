using DelegationStation.Authorization;
using DelegationStationShared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;

namespace DelegationStationTests.Authorization;

[TestClass]
public class DeviceTagAuthorizationHandlerTests
{
    [TestMethod]
    [DynamicData(nameof(UpdateActionData))]
    public async Task TestNoDelegation_Delegation_Admin(string name, List<Claim> claims, string defaultGroupId, DeviceTag tag, List<OperationAuthorizationRequirement> requirements, bool shouldPass)
    {
        // Arrange: Create a DeviceTagAuthorizationHandler
        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        // Arrange: Create a DeviceTagAuthorizationHandler
        var context = new AuthorizationHandlerContext(requirements, claimsPrincipal, tag);
        var myConfiguration = new Dictionary<string, string?>
                {
                    {"DefaultAdminGroupObjectId", defaultGroupId.ToString()},
                    {"Nested:Key1", "NestedValue1"},
                    {"Nested:Key2", "NestedValue2"}
                };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(myConfiguration)
            .Build();

        var handler = new DeviceTagAuthorizationHandler(configuration, NullLogger<DeviceTagAuthorizationHandler>.Instance);

        // Act: Handle the context
        await handler.HandleAsync(context);

        // Act: Add failure message to StringBuilder
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (var reason in context.FailureReasons)
        {
            sb.AppendLine(reason.Message.ToString());
        }

        // Assert

        Assert.AreEqual(shouldPass, context.HasSucceeded, sb.ToString());
    }

    [TestMethod]
    public async Task NoDefaultGroupShouldFail()
    {
        // Arrange: Create a DeviceTagAuthorizationHandler
        Guid defaultId = Guid.NewGuid();
        string delegatedGroupId = Guid.NewGuid().ToString();
        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString())
                    }));

        // Arrange: Create a DeviceTagAuthorizationHandler
        List<OperationAuthorizationRequirement> reqs = new List<OperationAuthorizationRequirement>();

        reqs.Add(DeviceTagOperations.Read);

        var context = new AuthorizationHandlerContext(reqs, claimsPrincipal, GetDeviceTag(TagRole.None, delegatedGroupId));
        var myConfiguration = new Dictionary<string, string?>
                {
                    {"Nested:Key1", "NestedValue1"},
                    {"Nested:Key2", "NestedValue2"}
                };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(myConfiguration)
            .Build();

        var handler = new DeviceTagAuthorizationHandler(configuration, NullLogger<DeviceTagAuthorizationHandler>.Instance);

        // Act: Handle the context
        await handler.HandleAsync(context);

        // Act: Add failure message to StringBuilder
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (var reason in context.FailureReasons)
        {
            sb.AppendLine(reason.Message.ToString());
        }

        // Assert

        Assert.AreEqual(false, context.HasSucceeded, sb.ToString());
        Assert.AreEqual("Unable to find default admin group", context.FailureReasons.First().Message);
    }

    public enum TagRole
    {
        None,
        All,
        Attributes_NoSecurityGroups_NoAdministrativeUnits,
        Attributes_SecurityGroups_NoAdministrativeUnits,
        Attributes_NoSecurityGroups_AdministrativeUnits,
        AttributesOdd_NoSecurityGroups_NoAdministrativeUnits,
        AttributesEven_NoSecurityGroups_NoAdministrativeUnits,
        NoAttributes_NoSecurityGroups_NoAdministrativeUnits,
        NoAttributes_SecurityGroups_NoAdministrativeUnits,
        NoAttributes_NoSecurityGroups_AdministrativeUnits,
    }

    public static DeviceTag GetDeviceTag(TagRole role, string securityGroupGuid)
    {
        switch (role)
        {
            case TagRole.None:
                return GetDeviceTagNone();
            case TagRole.All:
                return GetDeviceTagAll(securityGroupGuid);
            case TagRole.Attributes_NoSecurityGroups_NoAdministrativeUnits:
                return GetDeviceTagAttributesNoSecurityGroupsNoAdministrativeUnits(securityGroupGuid);
            case TagRole.Attributes_SecurityGroups_NoAdministrativeUnits:
                return GetDeviceTagAttributesSecurityGroupsNoAdministrativeUnits(securityGroupGuid);
            case TagRole.Attributes_NoSecurityGroups_AdministrativeUnits:
                return GetDeviceTagAttributesNoSecurityGroupsAdministrativeUnits(securityGroupGuid);
            case TagRole.AttributesOdd_NoSecurityGroups_NoAdministrativeUnits:
                return GetDeviceTagAttributesOddNoSecurityGroupsNoAdministrativeUnits(securityGroupGuid);
            case TagRole.AttributesEven_NoSecurityGroups_NoAdministrativeUnits:
                return GetDeviceTagAttributesEvenNoSecurityGroupsNoAdministrativeUnits(securityGroupGuid);
            case TagRole.NoAttributes_NoSecurityGroups_NoAdministrativeUnits:
                return GetDeviceTagNoAttributesNoSecurityGroupsNoAdministrativeUnits(securityGroupGuid);
            case TagRole.NoAttributes_SecurityGroups_NoAdministrativeUnits:
                return GetDeviceTagNoAttributesSecurityGroupsNoAdministrativeUnits(securityGroupGuid);
            case TagRole.NoAttributes_NoSecurityGroups_AdministrativeUnits:
                return GetDeviceTagNoAttributesNoSecurityGroupsAdministrativeUnits(securityGroupGuid);
            default:
                return GetDeviceTagNone();
        }
    }

    public static DeviceTag GetDeviceTagNone()
    {
        DeviceTag deviceTag = new DeviceTag();
        deviceTag.Name = "testTagName";
        deviceTag.Description = "testTagDescription";
        return deviceTag;
    }

    public static DeviceTag GetDeviceTagAll(string securityGroupGuid)
    {
        Role role = new Role();
        role.Name = "testRole";
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute1);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute2);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute3);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute4);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute5);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute6);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute7);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute8);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute9);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute10);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute11);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute12);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute13);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute14);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute15);
        role.SecurityGroups = true;
        role.AdministrativeUnits = true;
        RoleDelegation roleDelegation = new RoleDelegation();
        roleDelegation.SecurityGroupId = securityGroupGuid;
        roleDelegation.Role = role;
        DeviceTag deviceTag = new DeviceTag();
        deviceTag.Name = "testTagName";
        deviceTag.Description = "testTagDescription";
        deviceTag.RoleDelegations.Add(roleDelegation);
        return deviceTag;
    }

    public static DeviceTag GetDeviceTagAttributesNoSecurityGroupsNoAdministrativeUnits(string securityGroupGuid)
    {
        Role role = new Role();
        role.Name = "testRole";
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute1);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute2);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute3);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute4);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute5);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute6);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute7);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute8);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute9);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute10);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute11);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute12);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute13);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute14);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute15);
        role.SecurityGroups = false;
        role.AdministrativeUnits = false;
        RoleDelegation roleDelegation = new RoleDelegation();
        roleDelegation.SecurityGroupId = securityGroupGuid;
        roleDelegation.Role = role;
        DeviceTag deviceTag = new DeviceTag();
        deviceTag.Name = "testTagName";
        deviceTag.Description = "testTagDescription";
        deviceTag.RoleDelegations.Add(roleDelegation);
        return deviceTag;
    }

    public static DeviceTag GetDeviceTagAttributesSecurityGroupsNoAdministrativeUnits(string securityGroupGuid)
    {
        Role role = new Role();
        role.Name = "testRole";
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute1);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute2);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute3);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute4);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute5);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute6);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute7);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute8);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute9);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute10);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute11);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute12);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute13);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute14);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute15);
        role.SecurityGroups = true;
        role.AdministrativeUnits = false;
        RoleDelegation roleDelegation = new RoleDelegation();
        roleDelegation.SecurityGroupId = securityGroupGuid;
        roleDelegation.Role = role;
        DeviceTag deviceTag = new DeviceTag();
        deviceTag.Name = "testTagName";
        deviceTag.Description = "testTagDescription";
        deviceTag.RoleDelegations.Add(roleDelegation);
        return deviceTag;
    }

    public static DeviceTag GetDeviceTagAttributesNoSecurityGroupsAdministrativeUnits(string securityGroupGuid)
    {
        Role role = new Role();
        role.Name = "testRole";
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute1);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute2);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute3);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute4);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute5);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute6);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute7);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute8);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute9);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute10);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute11);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute12);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute13);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute14);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute15);
        role.SecurityGroups = false;
        role.AdministrativeUnits = true;
        RoleDelegation roleDelegation = new RoleDelegation();
        roleDelegation.SecurityGroupId = securityGroupGuid;
        roleDelegation.Role = role;
        DeviceTag deviceTag = new DeviceTag();
        deviceTag.Name = "testTagName";
        deviceTag.Description = "testTagDescription";
        deviceTag.RoleDelegations.Add(roleDelegation);
        return deviceTag;
    }

    public static DeviceTag GetDeviceTagAttributesOddNoSecurityGroupsNoAdministrativeUnits(string securityGroupGuid)
    {
        Role role = new Role();
        role.Name = "testRole";
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute1);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute2);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute3);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute4);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute5);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute6);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute7);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute8);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute9);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute10);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute11);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute12);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute13);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute14);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute15);
        role.SecurityGroups = false;
        role.AdministrativeUnits = false;
        RoleDelegation roleDelegation = new RoleDelegation();
        roleDelegation.SecurityGroupId = securityGroupGuid;
        roleDelegation.Role = role;
        DeviceTag deviceTag = new DeviceTag();
        deviceTag.Name = "testTagName";
        deviceTag.Description = "testTagDescription";
        deviceTag.RoleDelegations.Add(roleDelegation);
        return deviceTag;
    }

    public static DeviceTag GetDeviceTagAttributesEvenNoSecurityGroupsNoAdministrativeUnits(string securityGroupGuid)
    {
        Role role = new Role();
        role.Name = "testRole";
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute1);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute2);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute3);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute4);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute5);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute6);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute7);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute8);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute9);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute10);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute11);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute12);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute13);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute14);
        role.Attributes.Add(AllowedAttributes.ExtensionAttribute15);
        role.SecurityGroups = false;
        role.AdministrativeUnits = false;
        RoleDelegation roleDelegation = new RoleDelegation();
        roleDelegation.SecurityGroupId = securityGroupGuid;
        roleDelegation.Role = role;
        DeviceTag deviceTag = new DeviceTag();
        deviceTag.Name = "testTagName";
        deviceTag.Description = "testTagDescription";
        deviceTag.RoleDelegations.Add(roleDelegation);
        return deviceTag;
    }

    public static DeviceTag GetDeviceTagNoAttributesNoSecurityGroupsNoAdministrativeUnits(string securityGroupGuid)
    {
        Role role = new Role();
        role.Name = "testRole";
        role.SecurityGroups = false;
        role.AdministrativeUnits = false;
        RoleDelegation roleDelegation = new RoleDelegation();
        roleDelegation.SecurityGroupId = securityGroupGuid;
        roleDelegation.Role = role;
        DeviceTag deviceTag = new DeviceTag();
        deviceTag.Name = "testTagName";
        deviceTag.Description = "testTagDescription";
        deviceTag.RoleDelegations.Add(roleDelegation);
        return deviceTag;
    }

    public static DeviceTag GetDeviceTagNoAttributesSecurityGroupsNoAdministrativeUnits(string securityGroupGuid)
    {
        Role role = new Role();
        role.Name = "testRole";
        role.SecurityGroups = true;
        role.AdministrativeUnits = false;
        RoleDelegation roleDelegation = new RoleDelegation();
        roleDelegation.SecurityGroupId = securityGroupGuid;
        roleDelegation.Role = role;
        DeviceTag deviceTag = new DeviceTag();
        deviceTag.Name = "testTagName";
        deviceTag.Description = "testTagDescription";
        deviceTag.RoleDelegations.Add(roleDelegation);
        return deviceTag;
    }

    public static DeviceTag GetDeviceTagNoAttributesNoSecurityGroupsAdministrativeUnits(string securityGroupGuid)
    {
        Role role = new Role();
        role.Name = "testRole";
        role.SecurityGroups = false;
        role.AdministrativeUnits = true;
        RoleDelegation roleDelegation = new RoleDelegation();
        roleDelegation.SecurityGroupId = securityGroupGuid;
        roleDelegation.Role = role;
        DeviceTag deviceTag = new DeviceTag();
        deviceTag.Name = "testTagName";
        deviceTag.Description = "testTagDescription";
        deviceTag.RoleDelegations.Add(roleDelegation);
        return deviceTag;
    }

    public static IEnumerable<object[]> UpdateActionData
    {
        get
        {
            string defaultId = Guid.NewGuid().ToString();
            string delegatedGroupId = Guid.NewGuid().ToString();
            string noGroupId = Guid.NewGuid().ToString();

            return new[]
            {
                new object[] {
                    "Admin - No delegation - Check All",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.None, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.Update,
                        DeviceTagOperations.BulkUpload,
                        DeviceTagOperations.Delete,
                        DeviceTagOperations.UpdateActions,
                        DeviceTagOperations.UpdateAttributes,
                        DeviceTagOperations.UpdateSecurityGroups,
                        DeviceTagOperations.UpdateAdministrativeUnits,
                        DeviceTagOperations.Create,
                        DeviceTagOperations.Read,
                        DeviceTagOperations.UpdateExtensionAttribute1,
                        DeviceTagOperations.UpdateExtensionAttribute2,
                        DeviceTagOperations.UpdateExtensionAttribute3,
                        DeviceTagOperations.UpdateExtensionAttribute4,
                        DeviceTagOperations.UpdateExtensionAttribute5,
                        DeviceTagOperations.UpdateExtensionAttribute6,
                        DeviceTagOperations.UpdateExtensionAttribute7,
                        DeviceTagOperations.UpdateExtensionAttribute8,
                        DeviceTagOperations.UpdateExtensionAttribute9,
                        DeviceTagOperations.UpdateExtensionAttribute10,
                        DeviceTagOperations.UpdateExtensionAttribute11,
                        DeviceTagOperations.UpdateExtensionAttribute12,
                        DeviceTagOperations.UpdateExtensionAttribute13,
                        DeviceTagOperations.UpdateExtensionAttribute14,
                        DeviceTagOperations.UpdateExtensionAttribute15,
                    },
                    true
                },
                new object[] {
                    "Admin - All delegations - Check All",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.All, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.Update,
                        DeviceTagOperations.BulkUpload,
                        DeviceTagOperations.Delete,
                        DeviceTagOperations.UpdateActions,
                        DeviceTagOperations.UpdateAttributes,
                        DeviceTagOperations.UpdateSecurityGroups,
                        DeviceTagOperations.UpdateAdministrativeUnits,
                        DeviceTagOperations.Create,
                        DeviceTagOperations.Read,
                        DeviceTagOperations.UpdateExtensionAttribute1,
                        DeviceTagOperations.UpdateExtensionAttribute2,
                        DeviceTagOperations.UpdateExtensionAttribute3,
                        DeviceTagOperations.UpdateExtensionAttribute4,
                        DeviceTagOperations.UpdateExtensionAttribute5,
                        DeviceTagOperations.UpdateExtensionAttribute6,
                        DeviceTagOperations.UpdateExtensionAttribute7,
                        DeviceTagOperations.UpdateExtensionAttribute8,
                        DeviceTagOperations.UpdateExtensionAttribute9,
                        DeviceTagOperations.UpdateExtensionAttribute10,
                        DeviceTagOperations.UpdateExtensionAttribute11,
                        DeviceTagOperations.UpdateExtensionAttribute12,
                        DeviceTagOperations.UpdateExtensionAttribute13,
                        DeviceTagOperations.UpdateExtensionAttribute14,
                        DeviceTagOperations.UpdateExtensionAttribute15,
                    },
                    true
                },
                new object[] {
                    "Admin - Attribute delegations - Check All",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.Attributes_NoSecurityGroups_NoAdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.Update,
                        DeviceTagOperations.BulkUpload,
                        DeviceTagOperations.Delete,
                        DeviceTagOperations.UpdateActions,
                        DeviceTagOperations.UpdateAttributes,
                        DeviceTagOperations.UpdateSecurityGroups,
                        DeviceTagOperations.UpdateAdministrativeUnits,
                        DeviceTagOperations.Create,
                        DeviceTagOperations.Read,
                        DeviceTagOperations.UpdateExtensionAttribute1,
                        DeviceTagOperations.UpdateExtensionAttribute2,
                        DeviceTagOperations.UpdateExtensionAttribute3,
                        DeviceTagOperations.UpdateExtensionAttribute4,
                        DeviceTagOperations.UpdateExtensionAttribute5,
                        DeviceTagOperations.UpdateExtensionAttribute6,
                        DeviceTagOperations.UpdateExtensionAttribute7,
                        DeviceTagOperations.UpdateExtensionAttribute8,
                        DeviceTagOperations.UpdateExtensionAttribute9,
                        DeviceTagOperations.UpdateExtensionAttribute10,
                        DeviceTagOperations.UpdateExtensionAttribute11,
                        DeviceTagOperations.UpdateExtensionAttribute12,
                        DeviceTagOperations.UpdateExtensionAttribute13,
                        DeviceTagOperations.UpdateExtensionAttribute14,
                        DeviceTagOperations.UpdateExtensionAttribute15,
                    },
                    true
                },
                new object[] {
                    "Admin - Security Group delegations - Check All",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()),
                    },
                    defaultId,
                    GetDeviceTag(TagRole.Attributes_NoSecurityGroups_NoAdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.Update,
                        DeviceTagOperations.BulkUpload,
                        DeviceTagOperations.Delete,
                        DeviceTagOperations.UpdateActions,
                        DeviceTagOperations.UpdateAttributes,
                        DeviceTagOperations.UpdateSecurityGroups,
                        DeviceTagOperations.UpdateAdministrativeUnits,
                        DeviceTagOperations.Create,
                        DeviceTagOperations.Read,
                        DeviceTagOperations.UpdateExtensionAttribute1,
                        DeviceTagOperations.UpdateExtensionAttribute2,
                        DeviceTagOperations.UpdateExtensionAttribute3,
                        DeviceTagOperations.UpdateExtensionAttribute4,
                        DeviceTagOperations.UpdateExtensionAttribute5,
                        DeviceTagOperations.UpdateExtensionAttribute6,
                        DeviceTagOperations.UpdateExtensionAttribute7,
                        DeviceTagOperations.UpdateExtensionAttribute8,
                        DeviceTagOperations.UpdateExtensionAttribute9,
                        DeviceTagOperations.UpdateExtensionAttribute10,
                        DeviceTagOperations.UpdateExtensionAttribute11,
                        DeviceTagOperations.UpdateExtensionAttribute12,
                        DeviceTagOperations.UpdateExtensionAttribute13,
                        DeviceTagOperations.UpdateExtensionAttribute14,
                        DeviceTagOperations.UpdateExtensionAttribute15,
                    },
                    true
                },
                // User is not in the default group and no delegation
                new object[]
                {
                    "User - No delegation - All",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", noGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.None, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.Update,
                        DeviceTagOperations.BulkUpload,
                        DeviceTagOperations.Delete,
                        DeviceTagOperations.UpdateActions,
                        DeviceTagOperations.UpdateAttributes,
                        DeviceTagOperations.UpdateSecurityGroups,
                        DeviceTagOperations.UpdateAdministrativeUnits,
                        DeviceTagOperations.Create,
                        DeviceTagOperations.Read,
                        DeviceTagOperations.UpdateExtensionAttribute1,
                        DeviceTagOperations.UpdateExtensionAttribute2,
                        DeviceTagOperations.UpdateExtensionAttribute3,
                        DeviceTagOperations.UpdateExtensionAttribute4,
                        DeviceTagOperations.UpdateExtensionAttribute5,
                        DeviceTagOperations.UpdateExtensionAttribute6,
                        DeviceTagOperations.UpdateExtensionAttribute7,
                        DeviceTagOperations.UpdateExtensionAttribute8,
                        DeviceTagOperations.UpdateExtensionAttribute9,
                        DeviceTagOperations.UpdateExtensionAttribute10,
                        DeviceTagOperations.UpdateExtensionAttribute11,
                        DeviceTagOperations.UpdateExtensionAttribute12,
                        DeviceTagOperations.UpdateExtensionAttribute13,
                        DeviceTagOperations.UpdateExtensionAttribute14,
                        DeviceTagOperations.UpdateExtensionAttribute15,
                    },
                    false
                },
                new object[]
                {
                    "User - No delegation - Update",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", noGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.None, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.Update,
                    },
                    false
                },
                new object[]
                {
                    "User - No delegation - Read",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", noGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.None, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.Read,
                    },
                    false
                },
                new object[]
                {
                    "User - No delegation - Update Actions",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", noGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.None, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.UpdateActions,
                    },
                    false
                },
                new object[]
                {
                    "User - No delegation - Update Security Groups",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", noGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.None, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.UpdateSecurityGroups,
                    },
                    false
                },
                new object[]
                {
                    "User - No delegation - Update Administrative Units",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", noGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.None, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.UpdateAdministrativeUnits,
                    },
                    false
                },
                new object[]
                {
                    "User - No delegation - Update Attributes",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", noGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.None, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.UpdateAttributes,
                    },
                    false
                },
                // Delegated user is in delegated group
                new object[]
                {
                    "User - All - Fail",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.All, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.Update,
                    },
                    false
                },
                new object[]
                {
                    "User - All - Fail",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.All, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.UpdateActions,
                        DeviceTagOperations.Update,
                    },
                    false
                },
                new object[]
                {
                    "User - All - Pass",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.All, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.BulkUpload,
                        DeviceTagOperations.UpdateActions,
                        DeviceTagOperations.UpdateAttributes,
                        DeviceTagOperations.UpdateSecurityGroups,
                        DeviceTagOperations.UpdateAdministrativeUnits,
                        DeviceTagOperations.Read,
                        DeviceTagOperations.UpdateExtensionAttribute1,
                        DeviceTagOperations.UpdateExtensionAttribute2,
                        DeviceTagOperations.UpdateExtensionAttribute3,
                        DeviceTagOperations.UpdateExtensionAttribute4,
                        DeviceTagOperations.UpdateExtensionAttribute5,
                        DeviceTagOperations.UpdateExtensionAttribute6,
                        DeviceTagOperations.UpdateExtensionAttribute7,
                        DeviceTagOperations.UpdateExtensionAttribute8,
                        DeviceTagOperations.UpdateExtensionAttribute9,
                        DeviceTagOperations.UpdateExtensionAttribute10,
                        DeviceTagOperations.UpdateExtensionAttribute11,
                        DeviceTagOperations.UpdateExtensionAttribute12,
                        DeviceTagOperations.UpdateExtensionAttribute13,
                        DeviceTagOperations.UpdateExtensionAttribute14,
                        DeviceTagOperations.UpdateExtensionAttribute15,
                    },
                    true
                },
                new object[]
                {
                    "User - AttributesEven_NoSecurityGroups_NoAdministrativeUnits - Pass",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.AttributesEven_NoSecurityGroups_NoAdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.BulkUpload,
                        DeviceTagOperations.UpdateActions,
                        DeviceTagOperations.UpdateAttributes,
                        DeviceTagOperations.Read,
                        DeviceTagOperations.UpdateExtensionAttribute2,
                        DeviceTagOperations.UpdateExtensionAttribute4,
                        DeviceTagOperations.UpdateExtensionAttribute6,
                        DeviceTagOperations.UpdateExtensionAttribute8,
                        DeviceTagOperations.UpdateExtensionAttribute10,
                        DeviceTagOperations.UpdateExtensionAttribute12,
                        DeviceTagOperations.UpdateExtensionAttribute14,
                    },
                    true
                },
                new object[]
                {
                    "User - AttributesOdd_NoSecurityGroups_NoAdministrativeUnits - Pass",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.AttributesOdd_NoSecurityGroups_NoAdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.BulkUpload,
                        DeviceTagOperations.UpdateActions,
                        DeviceTagOperations.UpdateAttributes,
                        DeviceTagOperations.Read,
                        DeviceTagOperations.UpdateExtensionAttribute1,
                        DeviceTagOperations.UpdateExtensionAttribute3,
                        DeviceTagOperations.UpdateExtensionAttribute5,
                        DeviceTagOperations.UpdateExtensionAttribute7,
                        DeviceTagOperations.UpdateExtensionAttribute9,
                        DeviceTagOperations.UpdateExtensionAttribute11,
                        DeviceTagOperations.UpdateExtensionAttribute13,
                        DeviceTagOperations.UpdateExtensionAttribute15,
                    },
                    true
                },
                new object[]
                {
                    "User - AttributesOdd_NoSecurityGroups_NoAdministrativeUnits with Security Groups - Fail",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.AttributesOdd_NoSecurityGroups_NoAdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.BulkUpload,
                        DeviceTagOperations.UpdateActions,
                        DeviceTagOperations.UpdateAttributes,
                        DeviceTagOperations.Read,
                        DeviceTagOperations.UpdateExtensionAttribute1,
                        DeviceTagOperations.UpdateExtensionAttribute3,
                        DeviceTagOperations.UpdateExtensionAttribute5,
                        DeviceTagOperations.UpdateExtensionAttribute7,
                        DeviceTagOperations.UpdateExtensionAttribute9,
                        DeviceTagOperations.UpdateExtensionAttribute11,
                        DeviceTagOperations.UpdateExtensionAttribute13,
                        DeviceTagOperations.UpdateExtensionAttribute15,
                        DeviceTagOperations.UpdateSecurityGroups
                    },
                    false
                },
                new object[]
                {
                    "User - AttributesOdd_NoSecurityGroups_NoAdministrativeUnits with Administrative Units - Fail",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.AttributesOdd_NoSecurityGroups_NoAdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.BulkUpload,
                        DeviceTagOperations.UpdateActions,
                        DeviceTagOperations.UpdateAttributes,
                        DeviceTagOperations.Read,
                        DeviceTagOperations.UpdateExtensionAttribute1,
                        DeviceTagOperations.UpdateExtensionAttribute3,
                        DeviceTagOperations.UpdateExtensionAttribute5,
                        DeviceTagOperations.UpdateExtensionAttribute7,
                        DeviceTagOperations.UpdateExtensionAttribute9,
                        DeviceTagOperations.UpdateExtensionAttribute11,
                        DeviceTagOperations.UpdateExtensionAttribute13,
                        DeviceTagOperations.UpdateExtensionAttribute15,
                        DeviceTagOperations.UpdateAdministrativeUnits,
                    },
                    false
                },
                new object[]
                {
                    "User - Attributes_NoSecurityGroups_AdministrativeUnits - Pass",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.Attributes_NoSecurityGroups_AdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.BulkUpload,
                        DeviceTagOperations.UpdateActions,
                        DeviceTagOperations.UpdateAttributes,
                        DeviceTagOperations.Read,
                        DeviceTagOperations.UpdateExtensionAttribute1,
                        DeviceTagOperations.UpdateExtensionAttribute2,
                        DeviceTagOperations.UpdateExtensionAttribute3,
                        DeviceTagOperations.UpdateExtensionAttribute4,
                        DeviceTagOperations.UpdateExtensionAttribute5,
                        DeviceTagOperations.UpdateExtensionAttribute6,
                        DeviceTagOperations.UpdateExtensionAttribute7,
                        DeviceTagOperations.UpdateExtensionAttribute8,
                        DeviceTagOperations.UpdateExtensionAttribute9,
                        DeviceTagOperations.UpdateExtensionAttribute10,
                        DeviceTagOperations.UpdateExtensionAttribute11,
                        DeviceTagOperations.UpdateExtensionAttribute12,
                        DeviceTagOperations.UpdateExtensionAttribute13,
                        DeviceTagOperations.UpdateExtensionAttribute14,
                        DeviceTagOperations.UpdateExtensionAttribute15,
                        DeviceTagOperations.UpdateAdministrativeUnits,
                    },
                    true
                },
                new object[]
                {
                    "User - Attributes_NoSecurityGroups_AdministrativeUnits - Fail",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.Attributes_NoSecurityGroups_AdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.BulkUpload,
                        DeviceTagOperations.UpdateActions,
                        DeviceTagOperations.UpdateAttributes,
                        DeviceTagOperations.Read,
                        DeviceTagOperations.UpdateExtensionAttribute1,
                        DeviceTagOperations.UpdateExtensionAttribute2,
                        DeviceTagOperations.UpdateExtensionAttribute3,
                        DeviceTagOperations.UpdateExtensionAttribute4,
                        DeviceTagOperations.UpdateExtensionAttribute5,
                        DeviceTagOperations.UpdateExtensionAttribute6,
                        DeviceTagOperations.UpdateExtensionAttribute7,
                        DeviceTagOperations.UpdateExtensionAttribute8,
                        DeviceTagOperations.UpdateExtensionAttribute9,
                        DeviceTagOperations.UpdateExtensionAttribute10,
                        DeviceTagOperations.UpdateExtensionAttribute11,
                        DeviceTagOperations.UpdateExtensionAttribute12,
                        DeviceTagOperations.UpdateExtensionAttribute13,
                        DeviceTagOperations.UpdateExtensionAttribute14,
                        DeviceTagOperations.UpdateExtensionAttribute15,
                        DeviceTagOperations.UpdateAdministrativeUnits,
                        DeviceTagOperations.UpdateSecurityGroups,
                    },
                    false
                },
                new object[]
                {
                    "User - Attributes_SecurityGroups_NoAdministrativeUnits - Pass",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.Attributes_SecurityGroups_NoAdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.BulkUpload,
                        DeviceTagOperations.UpdateActions,
                        DeviceTagOperations.UpdateAttributes,
                        DeviceTagOperations.Read,
                        DeviceTagOperations.UpdateExtensionAttribute1,
                        DeviceTagOperations.UpdateExtensionAttribute2,
                        DeviceTagOperations.UpdateExtensionAttribute3,
                        DeviceTagOperations.UpdateExtensionAttribute4,
                        DeviceTagOperations.UpdateExtensionAttribute5,
                        DeviceTagOperations.UpdateExtensionAttribute6,
                        DeviceTagOperations.UpdateExtensionAttribute7,
                        DeviceTagOperations.UpdateExtensionAttribute8,
                        DeviceTagOperations.UpdateExtensionAttribute9,
                        DeviceTagOperations.UpdateExtensionAttribute10,
                        DeviceTagOperations.UpdateExtensionAttribute11,
                        DeviceTagOperations.UpdateExtensionAttribute12,
                        DeviceTagOperations.UpdateExtensionAttribute13,
                        DeviceTagOperations.UpdateExtensionAttribute14,
                        DeviceTagOperations.UpdateExtensionAttribute15,
                        DeviceTagOperations.UpdateSecurityGroups,
                    },
                    true
                },
                new object[]
                {
                    "User - Attributes_SecurityGroups_NoAdministrativeUnits - Fail",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.Attributes_SecurityGroups_NoAdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.BulkUpload,
                        DeviceTagOperations.UpdateActions,
                        DeviceTagOperations.UpdateAttributes,
                        DeviceTagOperations.Read,
                        DeviceTagOperations.UpdateExtensionAttribute1,
                        DeviceTagOperations.UpdateExtensionAttribute2,
                        DeviceTagOperations.UpdateExtensionAttribute3,
                        DeviceTagOperations.UpdateExtensionAttribute4,
                        DeviceTagOperations.UpdateExtensionAttribute5,
                        DeviceTagOperations.UpdateExtensionAttribute6,
                        DeviceTagOperations.UpdateExtensionAttribute7,
                        DeviceTagOperations.UpdateExtensionAttribute8,
                        DeviceTagOperations.UpdateExtensionAttribute9,
                        DeviceTagOperations.UpdateExtensionAttribute10,
                        DeviceTagOperations.UpdateExtensionAttribute11,
                        DeviceTagOperations.UpdateExtensionAttribute12,
                        DeviceTagOperations.UpdateExtensionAttribute13,
                        DeviceTagOperations.UpdateExtensionAttribute14,
                        DeviceTagOperations.UpdateExtensionAttribute15,
                        DeviceTagOperations.UpdateSecurityGroups,
                        DeviceTagOperations.UpdateAdministrativeUnits,
                    },
                    false
                },
                // NoAttributes_NoSecurityGroups_NoAdministrativeUnits
                new object[]
                {
                    "User - NoAttributes_NoSecurityGroups_NoAdministrativeUnits - Pass",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.NoAttributes_NoSecurityGroups_NoAdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.BulkUpload,
                        DeviceTagOperations.Read,
                    },
                    true
                },
                new object[]
                {
                    "User - NoAttributes_NoSecurityGroups_NoAdministrativeUnits with Update - Fail",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.NoAttributes_NoSecurityGroups_NoAdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.Update,
                    },
                    false
                },
                new object[]
                {
                    "User - NoAttributes_NoSecurityGroups_NoAdministrativeUnits with Update Actions - Fail",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.NoAttributes_NoSecurityGroups_NoAdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.UpdateActions,
                    },
                    false
                },
                new object[]
                {
                    "User - NoAttributes_NoSecurityGroups_NoAdministrativeUnits with Security Groups - Fail",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.NoAttributes_NoSecurityGroups_NoAdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.UpdateSecurityGroups,
                    },
                    false
                },
                new object[]
                {
                    "User - NoAttributes_NoSecurityGroups_NoAdministrativeUnits with Update Actions - Fail",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.NoAttributes_NoSecurityGroups_NoAdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.UpdateActions,
                    },
                    false
                },
                new object[]
                {
                    "User - NoAttributes_NoSecurityGroups_NoAdministrativeUnits with Administrative Units - Fail",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.NoAttributes_NoSecurityGroups_NoAdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.UpdateAdministrativeUnits,
                    },
                    false
                },
                new object[]
                {
                    "User - NoAttributes_NoSecurityGroups_NoAdministrativeUnits with Attributes - Fail",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.NoAttributes_NoSecurityGroups_NoAdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.UpdateAttributes,
                    },
                    false
                },
                // NoAttributes_SecurityGroups_NoAdministrativeUnits
                new object[]
                {
                    "User - NoAttributes_SecurityGroups_NoAdministrativeUnits - Pass",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.NoAttributes_SecurityGroups_NoAdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.UpdateActions,
                        DeviceTagOperations.BulkUpload,
                        DeviceTagOperations.Read,
                        DeviceTagOperations.UpdateSecurityGroups,
                    },
                    true
                },
                new object[]
                {
                    "User - NoAttributes_SecurityGroups_NoAdministrativeUnits with Update - Fail",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.NoAttributes_SecurityGroups_NoAdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.Update,
                    },
                    false
                },
                new object[]
                {
                    "User - NoAttributes_SecurityGroups_NoAdministrativeUnits with Administrative Units - Fail",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.NoAttributes_SecurityGroups_NoAdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.UpdateAdministrativeUnits,
                    },
                    false
                },
                new object[]
                {
                    "User - NoAttributes_SecurityGroups_NoAdministrativeUnits with Attributes - Fail",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.NoAttributes_SecurityGroups_NoAdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.UpdateAttributes,
                    },
                    false
                },
                // NoAttributes_NoSecurityGroups_AdministrativeUnits
                new object[]
                {
                    "User - NoAttributes_NoSecurityGroups_AdministrativeUnits - Pass",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.NoAttributes_NoSecurityGroups_AdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.UpdateActions,
                        DeviceTagOperations.BulkUpload,
                        DeviceTagOperations.Read,
                        DeviceTagOperations.UpdateAdministrativeUnits,
                    },
                    true
                },
                new object[]
                {
                    "User - NoAttributes_NoSecurityGroups_AdministrativeUnits with Update - Fail",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.NoAttributes_NoSecurityGroups_AdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.Update,
                    },
                    false
                },
                new object[]
                {
                    "User - NoAttributes_NoSecurityGroups_AdministrativeUnits with Security Groups - Fail",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.NoAttributes_NoSecurityGroups_AdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.UpdateSecurityGroups,
                    },
                    false
                },
                new object[]
                {
                    "User - NoAttributes_NoSecurityGroups_AdministrativeUnits with Attributes - Fail",
                    new List<Claim>
                    {
                        new Claim("name", "TEST USER"),
                        new Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", delegatedGroupId.ToString())
                    },
                    defaultId,
                    GetDeviceTag(TagRole.NoAttributes_NoSecurityGroups_AdministrativeUnits, delegatedGroupId),
                    new List<OperationAuthorizationRequirement>
                    {
                        DeviceTagOperations.UpdateAttributes,
                    },
                    false
                },
            };
        }
    }


}
