using DelegationStation.Pages;
using Microsoft.Extensions.DependencyInjection;
using DelegationStation.Interfaces;
using Microsoft.QualityTools.Testing.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using DelegationStationShared.Enums;

namespace DelegationStationTests.Pages
{
    [TestClass]
    public class RoleTests : Bunit.TestContext
    {
        [TestMethod]
        public void RolesShouldRender()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                Guid userGroupId = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));
                authContext.SetPolicies("DelegationStationAdmin");
                //      Create fake services
                List<Role> roles = new List<Role>();
                Role role = new Role();
                role.Name = "testRole";
                role.Attributes.Add(AllowedAttributes.ExtensionAttribute1);
                role.Attributes.Add(AllowedAttributes.ExtensionAttribute5);
                role.Attributes.Add(AllowedAttributes.ExtensionAttribute12);
                role.SecurityGroups = false;
                role.AdministrativeUnits = false;
                roles.Add(role);

                RoleDelegation roleDelegation = new RoleDelegation();
                roleDelegation.SecurityGroupId = userGroupId.ToString();
                roleDelegation.Role = role;

                var fakeDeviceRoleDBService = new DelegationStation.Interfaces.Fakes.StubIRoleDBService()
                {
                    GetRolesAsync = () => Task.FromResult(roles)
                };

                var myConfiguration = new Dictionary<string, string?>
                {
                    {"DefaultAdminGroupObjectId", defaultId.ToString()},
                    {"Nested:Key1", "NestedValue1"},
                    {"Nested:Key2", "NestedValue2"}
                };

                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(myConfiguration)
                    .Build();

                var httpContext = new HttpContextAccessor();
                httpContext.HttpContext = new DefaultHttpContext();

                //      Add Dependent Services
                Services.AddSingleton<IRoleDBService>(fakeDeviceRoleDBService);
                Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
                Services.AddSingleton<IHttpContextAccessor>(httpContext);


                // Act
                var cut = RenderComponent<Roles>();

                // Assert
                Assert.IsTrue(cut.Markup.Contains("testRole"), $"testRole should be rendered. \\nActual:\\n{cut.Markup}\"");
                Assert.IsTrue(cut.Markup.Contains("ExtensionAttribute1"), $"Extension attribute 1 should be rendered.\\nActual:\\n{cut.Markup}");
                Assert.IsTrue(cut.Markup.Contains("ExtensionAttribute5"), $"Extension attribute 5 should be rendered.\\nActual:\\n{cut.Markup}");
                Assert.IsTrue(cut.Markup.Contains("ExtensionAttribute12"), $"Extension attribute 12 should be rendered.\\nActual:\\n{cut.Markup}");

                Assert.IsFalse(cut.Markup.Contains("Not Authorized"), $"Page should not show Not Authorized. \\nActual:\\n{cut.Markup}\"");

            }
        }

        [TestMethod]
        public void UnauthorizedShouldNotRender()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                Guid userGroupId = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", userGroupId.ToString()));

                //      Create fake services
                List<Role> roles = new List<Role>();
                Role role = new Role();
                role.Name = "testRole";
                role.Attributes.Add(AllowedAttributes.ExtensionAttribute1);
                role.Attributes.Add(AllowedAttributes.ExtensionAttribute5);
                role.Attributes.Add(AllowedAttributes.ExtensionAttribute12);
                role.SecurityGroups = false;
                role.AdministrativeUnits = false;
                roles.Add(role);

                RoleDelegation roleDelegation = new RoleDelegation();
                roleDelegation.SecurityGroupId = userGroupId.ToString();
                roleDelegation.Role = role;

                var fakeDeviceRoleDBService = new DelegationStation.Interfaces.Fakes.StubIRoleDBService()
                {
                    GetRolesAsync = () => Task.FromResult(roles)
                };

                var myConfiguration = new Dictionary<string, string?>
                {
                    {"DefaultAdminGroupObjectId", defaultId.ToString()},
                    {"Nested:Key1", "NestedValue1"},
                    {"Nested:Key2", "NestedValue2"}
                };

                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(myConfiguration)
                    .Build();

                var httpContext = new HttpContextAccessor();
                httpContext.HttpContext = new DefaultHttpContext();

                //      Add Dependent Services
                Services.AddSingleton<IRoleDBService>(fakeDeviceRoleDBService);
                Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
                Services.AddSingleton<IHttpContextAccessor>(httpContext);


                // Act
                var cut = RenderComponent<Roles>();

                // Assert
                Assert.IsTrue(cut.Markup.Contains("Not Authorized"), $"Page should show Not Authorized. \\nActual:\\n{cut.Markup}\"");
                Assert.IsFalse(cut.Markup.Contains("testRole"));
            }
        }
    }
}