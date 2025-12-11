using DelegationStation.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.QualityTools.Testing.Fakes;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using DelegationStation.Authorization;
using DelegationStation.Interfaces;
using DelegationStationShared.Enums;

namespace DelegationStationTests.Pages
{
    [TestClass]
    public class TagEditTests : Bunit.TestContext
    {
        [TestMethod]
        public void TagShouldRender()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();

                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));
                authContext.SetPolicies("TagView", "TagUpdate");

                AddDefaultServices(defaultId.ToString());

                // Act
                var cut = RenderComponent<TagEdit>(parameters => parameters
                    .Add(p => p.Id, "11111111-1111-1111-1111-111111111111"));

                // Assert
                string match = @"class=""form-control valid"" value=""testTagName1""";
                Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
                match = @"class=""form-control valid"" value=""testTagDescription1""";
                Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
            }
        }
        [TestMethod]
        public void AdminShouldRenderEdit()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                AddDefaultServices(defaultId.ToString());
                var authContext = this.AddTestAuthorization();

                authContext.SetAuthorized("TEST USER", AuthorizationState.Authorized);
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));
                authContext.SetPolicies("TagView", "TagUpdate");

                // Act
                var cut = RenderComponent<TagEdit>(parameters => parameters
                    .Add(p => p.Id, Guid.NewGuid().ToString()));

                // Assert
                string match = @"Role:\s*<select";
                Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void AuthorizedShouldNotRenderEdit()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", Guid.NewGuid().ToString()));
                AddDefaultServices(defaultId.ToString());

                // Act
                var cut = RenderComponent<TagEdit>(parameters => parameters
                    .Add(p => p.Id, "myId"));

                // Assert
                string match = @" Role:
                    <select";
                Assert.IsFalse(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void AdminShouldShowAttributes()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));
                authContext.SetPolicies("TagView", "TagUpdate", "TagUpdateActions", "TagUpdateActionAttributes");
                AddDefaultServices(defaultId.ToString());

                // Act
                var cut = RenderComponent<TagEdit>(parameters => parameters
                    .Add(p => p.Id, "myId"));

                // Assert
                string match = $"<option value=\"Attribute\".*>Attribute</option>";
                Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void AdminShouldShowSecurityGroups()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));
                authContext.SetPolicies("TagView", "TagUpdate", "TagUpdateActions", "TagUpdateActionSecurityGroups");

                AddDefaultServices(defaultId.ToString());

                // Act
                var cut = RenderComponent<TagEdit>(parameters => parameters
                    .Add(p => p.Id, "myId"));

                // Assert
                string match = $"<option value=\"Group\".*>Group</option>";
                Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void AdminShouldShowAdministrativeUnits()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));
                authContext.SetPolicies("TagView", "TagUpdate", "TagUpdateActions", "TagUpdateActionAdministrativeUnits");
                AddDefaultServices(defaultId.ToString());

                // Act
                var cut = RenderComponent<TagEdit>(parameters => parameters
                    .Add(p => p.Id, "myId"));

                // Assert
                string match = $"<option value=\"AdministrativeUnit\".*>AdministrativeUnit</option>";
                Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void LimitedRoleShouldNotRenderSecurityGroups()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                Guid userGroup = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", userGroup.ToString()));
                authContext.SetPolicies("TagView");

                AddLimitedRoleServices(defaultId.ToString(), userGroup.ToString());

                // Act
                var cut = RenderComponent<TagEdit>(parameters => parameters
                    .Add(p => p.Id, "myId"));

                // Assert
                string match = $"<option value=\"Group\".*>Group</option>";
                Assert.IsFalse(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void LimitedRoleShouldNotRenderAdministrativeUnits()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                Guid userGroup = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", userGroup.ToString()));
                authContext.SetClaims(new System.Security.Claims.Claim("roles", userGroup.ToString()));
                authContext.SetPolicies("TagView");

                AddLimitedRoleServices(defaultId.ToString(), userGroup.ToString());

                // Act
                var cut = RenderComponent<TagEdit>(parameters => parameters
                    .Add(p => p.Id, "myId"));

                // Assert
                string match = $"<option value=\"AdministrativeUnit\".*>AdministrativeUnit</option>";
                Assert.IsFalse(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void LimitedRoleShouldNotRenderAttributes()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                Guid userGroup = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", userGroup.ToString()));
                authContext.SetClaims(new System.Security.Claims.Claim("roles", userGroup.ToString()));
                authContext.SetPolicies("TagView");

                AddLimitedRoleServices(defaultId.ToString(), userGroup.ToString());

                // Act
                var cut = RenderComponent<TagEdit>(parameters => parameters
                    .Add(p => p.Id, "myId"));

                // Assert
                string match = $"<option value=\"Attribute\".*>Attribute</option>";
                Assert.IsFalse(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void UnauthorizedShouldNotRender()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetNotAuthorized();

                AddDefaultServices(defaultId.ToString());

                // Act
                var cut = RenderComponent<TagEdit>();

                // Assert
                string match = @"<h3>Tag Edit</h3>.*";
                Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
                match = @"<h3>Not Authorized</h3>";
                Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
                match = @".*<table";
                Assert.IsFalse(Regex.IsMatch(cut.Markup, match), $"Expected to not Match:\n{match}\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void NoIdParameterShouldError()
        {
            // Arrange
            Guid defaultId = Guid.NewGuid();
            Guid userGroup = Guid.NewGuid();
            var authContext = this.AddTestAuthorization();
            authContext.SetAuthorized("TEST USER");
            authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
            authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", userGroup.ToString()));
            authContext.SetPolicies("TagView");

            AddDefaultServices(defaultId.ToString());

            // Act
            var cut = RenderComponent<TagEdit>();


            // Assert
            string match = @"<h3>Tag Edit</h3>.*";
            Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
            match = @"<h3>Error in navigation path</h3>";
            Assert.IsTrue(Regex.IsMatch(cut.Markup, match), $"Expected Match:\n{match}\nActual:\n{cut.Markup}");
        }

        [TestMethod]
        [ExpectedException(typeof(Bunit.ElementNotFoundException))]
        public void SaveButtonShouldNotRender()
        {
            // Arrange
            Guid defaultId = Guid.NewGuid();
            Guid userGroup = Guid.NewGuid();
            var authContext = this.AddTestAuthorization();
            authContext.SetAuthorized("TEST USER");
            authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
            authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", userGroup.ToString()));

            AddDefaultServices(defaultId.ToString());

            // Act
            var cut = RenderComponent<TagEdit>();
            var buttonElement = cut.Find("#SaveButton");

            // Assert
            Assert.IsNull(buttonElement);
        }

        [TestMethod]
        public void SaveButtonShouldRender()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                // Add Dependent Services
                Guid defaultId = Guid.NewGuid();
                Guid userGroup = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", userGroup.ToString()));
                authContext.SetPolicies("TagView", "TagUpdateActions");

                AddLimitedRoleServices(defaultId.ToString(), userGroup.ToString());

                // Act
                var cut = RenderComponent<TagEdit>(parameters => parameters
                    .Add(p => p.Id, "myId"));
                var buttonElement = cut.Find("#SaveButton");
                //FIXME:  Do we actually need this to test?
                //buttonElement.Click();

                // Assert
                Assert.IsNotNull(buttonElement);
            }
        }

        private void AddDefaultServices(string defaultId = "")
        {

            //      Create fake services
            List<DeviceTag> deviceTags = new List<DeviceTag>();
            DeviceTag deviceTag1 = new DeviceTag();
            deviceTag1.Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
            deviceTag1.Name = "testTagName1";
            deviceTag1.Description = "testTagDescription1";
            deviceTags.Add(deviceTag1);
            DeviceTag deviceTag2 = new DeviceTag();
            deviceTag2.Id = Guid.Parse("22222222-2222-2222-2222-222222222222");
            deviceTag2.Name = "testName2";
            deviceTag2.Description = "testDescription2";
            deviceTags.Add(deviceTag2);
            var fakeDeviceTagDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceTagDBService()
            {
                GetDeviceTagsAsyncIEnumerableOfStringString =
                    (groupIds, name) => Task.FromResult<List<DeviceTag>>(deviceTags),
                GetDeviceTagAsyncString =
                    (input) => Task.FromResult(deviceTag1)
            };

            DelegationStationShared.Models.Device device1 = new DelegationStationShared.Models.Device()
            {
                Make = "1",
                Model = "2",
                SerialNumber = "3"
            };
            List<DelegationStationShared.Models.Device> devices = new List<DelegationStationShared.Models.Device>();
            devices.Add(device1);
            var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
            {
                GetDevicesAsyncIEnumerableOfStringStringInt32Int32 = (a, b, c, d) =>
                    Task.FromResult(devices)
            };

            List<Role> roles = new List<Role>();
            Role role = new Role();
            role.Name = "testRole";
            role.Attributes.Add(AllowedAttributes.All);
            role.SecurityGroups = true;
            role.AdministrativeUnits = true;
            roles.Add(role);

            var fakeRoleDBService = new DelegationStation.Interfaces.Fakes.StubIRoleDBService()
            {
                GetRolesAsync = () => Task.FromResult<List<Role>>(roles)
            };

            var fakeGraphService = new DelegationStation.Interfaces.Fakes.StubIGraphService()
            {
                GetSecurityGroupNameString = (input) => Task.FromResult((string)input)
            };

            var myConfiguration = new Dictionary<string, string?>
                {
                    {"DefaultAdminGroupObjectId", defaultId},
                    {"Nested:Key1", "NestedValue1"},
                    {"Nested:Key2", "NestedValue2"}
                };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(myConfiguration)
                .Build();



            //      Add Dependent Services
            Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
            Services.AddSingleton<IAuthorizationHandler, DeviceTagAuthorizationHandler>();
            Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
            Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
            Services.AddSingleton<IRoleDBService>(fakeRoleDBService);
            Services.AddSingleton<IGraphService>(fakeGraphService);
        }

        private void AddLimitedRoleServices(string defaultId = "", string userGroupId = "")
        {

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
            roleDelegation.SecurityGroupId = userGroupId;
            roleDelegation.Role = role;

            List<DeviceTag> deviceTags = new List<DeviceTag>();
            DeviceTag deviceTag1 = new DeviceTag();
            deviceTag1.Name = "testTagName1";
            deviceTag1.Description = "testTagDescription1";
            deviceTag1.RoleDelegations.Add(roleDelegation);
            deviceTags.Add(deviceTag1);
            DeviceTag deviceTag2 = new DeviceTag();
            deviceTag2.Name = "testName2";
            deviceTag2.Description = "testDescription2";
            deviceTags.Add(deviceTag2);
            var fakeDeviceTagDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceTagDBService()
            {
                GetDeviceTagsAsyncIEnumerableOfStringString =
                    (groupIds, name) => Task.FromResult<List<DeviceTag>>(deviceTags),
                GetDeviceTagAsyncString =
                    (input) => Task.FromResult(deviceTag1)
            };

            DelegationStationShared.Models.Device device1 = new DelegationStationShared.Models.Device()
            {
                Make = "1",
                Model = "2",
                SerialNumber = "3"
            };
            List<DelegationStationShared.Models.Device> devices = new List<DelegationStationShared.Models.Device>();
            devices.Add(device1);
            var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
            {
                GetDevicesAsyncIEnumerableOfStringStringInt32Int32 = (a, b, c, d) =>
                    Task.FromResult(devices)
            };



            var fakeRoleDBService = new DelegationStation.Interfaces.Fakes.StubIRoleDBService()
            {
                GetRolesAsync = () => Task.FromResult<List<Role>>(roles)
            };

            var fakeGraphService = new DelegationStation.Interfaces.Fakes.StubIGraphService()
            {
                GetSecurityGroupNameString = (input) => Task.FromResult((string)input)
            };

            var myConfiguration = new Dictionary<string, string?>
                {
                    {"DefaultAdminGroupObjectId", defaultId},
                    {"Nested:Key1", "NestedValue1"},
                    {"Nested:Key2", "NestedValue2"}
                };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(myConfiguration)
                .Build();


            //      Add Dependent Services
            Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
            Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
            Services.AddSingleton<IRoleDBService>(fakeRoleDBService);
            Services.AddSingleton<IGraphService>(fakeGraphService);
            Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
            Services.AddSingleton<IAuthorizationHandler, DeviceTagAuthorizationHandler>();

        }
    }
}