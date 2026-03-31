using DelegationStation.Interfaces;
using DelegationStation.Pages;
using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.QualityTools.Testing.Fakes;
using System.Text.RegularExpressions;

namespace DelegationStationTests.Pages
{
    [TestClass]
    public class DevicesTests : Bunit.TestContext
    {
        [TestMethod]
        public void DevicesShouldRender()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                AddDefaultServices(defaultId.ToString());

                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                // Create fake services
                List<DeviceTag> deviceTags = new List<DeviceTag>();
                DeviceTag deviceTag = new DeviceTag();
                deviceTag.Name = "testName";
                deviceTag.Description = "testDescription";
                deviceTags.Add(deviceTag);

                var fakeDeviceTagDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceTagDBService()
                {
                    GetDeviceTagsAsyncIEnumerableOfStringString =
                        (groupIds, name) => Task.FromResult(deviceTags),
                    GetDeviceTagCountAsyncIEnumerableOfStringString =
                        (groupIds, name) => Task.FromResult(2),
                    GetDeviceTagsByPageAsyncIEnumerableOfStringInt32Int32String =
                        (groupIds, pageNumber, pageSize, name) => Task.FromResult(deviceTags)
                };

                List<Device> devices = new List<Device>();
                Device device = new Device();
                device.Make = "testMake";
                device.Model = "testModel";
                device.SerialNumber = "1111";
                device.PreferredHostname = "testHostname";
                device.Tags.Add(deviceTag.Name);
                devices.Add(device);

                var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
                {
                    GetDevicesAsyncIEnumerableOfStringInt32Int32 =
                        (groupIds,pageSize,currentPage) => Task.FromResult(devices)
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


                // Add Dependent Services
                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
                Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);

                // Act
                var cut = RenderComponent<Devices>();

                // Assert
                Assert.IsTrue(cut.Markup.Contains("testMake</td>"), $"testMake should be rendered in the table as a Make. \\nActual:\\n{cut.Markup}\"");
                Assert.IsTrue(cut.Markup.Contains("testModel</td>"), "testModel should be rendered in the table as a Model.");
                Assert.IsTrue(cut.Markup.Contains("1111</td>"), "1111 should be rendered in the table as a serialNumber.");
                Assert.IsTrue(cut.Markup.Contains("testHostname</td>"), "testHostname should be rendered in the table as a Hostname.");
            }
        }

        [TestMethod]
        public void UnauthorizedShouldNotRender()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                AddDefaultServices();

                var authContext = this.AddTestAuthorization();
                authContext.SetNotAuthorized();

                // Act
                var cut = RenderComponent<Devices>();

                // Assert
                cut.MarkupMatches(@"
<h3>Devices</h3>

<p>Not Authorized</p>");
            }
        }

        [TestMethod]
        public void SearchShouldOnlyReturnDevicesForAuthorizedTags()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid userGroupId = Guid.NewGuid();
                Guid defaultId = Guid.NewGuid(); // Different from userGroupId: user is not a default admin

                var authContext = this.AddTestAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", userGroupId.ToString()));

                // Tag the user has access to
                DeviceTag authorizedTag = new DeviceTag() { Name = "AuthorizedTag" };
                // Tag the user does NOT have access to
                DeviceTag unauthorizedTag = new DeviceTag() { Name = "UnauthorizedTag" };

                List<DeviceTag> userAccessibleTags = new List<DeviceTag>() { authorizedTag };

                var fakeDeviceTagDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceTagDBService()
                {
                    GetDeviceTagsAsyncIEnumerableOfStringString =
                        (groupIds, name) => Task.FromResult(userAccessibleTags),
                    GetDeviceTagCountAsyncIEnumerableOfStringString =
                        (groupIds, name) => Task.FromResult(1),
                    GetDeviceTagsByPageAsyncIEnumerableOfStringInt32Int32String =
                        (groupIds, pageNumber, pageSize, name) => Task.FromResult(userAccessibleTags)
                };

                // Device the user IS authorized to see (tagged with the authorized tag)
                Device authorizedDevice = new Device()
                {
                    Make = "AuthorizedMake",
                    Model = "AuthorizedModel",
                    SerialNumber = "AUTH-001",
                    Tags = new List<string>() { authorizedTag.Id.ToString() }
                };

                // Device the user is NOT authorized to see (tagged with an inaccessible tag)
                Device unauthorizedDevice = new Device()
                {
                    Make = "UnauthorizedMake",
                    Model = "UnauthorizedModel",
                    SerialNumber = "UNAUTH-001",
                    Tags = new List<string>() { unauthorizedTag.Id.ToString() }
                };

                IEnumerable<string>? capturedGroupIds = null;

                var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
                {
                    // Initial page load returns nothing so search results are clearly distinguishable
                    GetDevicesAsyncIEnumerableOfStringInt32Int32 =
                        (groupIds, pageSize, currentPage) => Task.FromResult(new List<Device>()),
                    // Search stub: capture the groupIds passed and return only the authorized device
                    GetDevicesSearchAsyncIEnumerableOfStringStringStringStringNullableOfInt32String =
                        (groupIds, make, model, serialNumber, osID, preferredHostname) =>
                        {
                            capturedGroupIds = groupIds;
                            return Task.FromResult(new List<Device>() { authorizedDevice });
                        }
                };

                var myConfiguration = new Dictionary<string, string?>
                {
                    { "DefaultAdminGroupObjectId", defaultId.ToString() }
                };

                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(myConfiguration)
                    .Build();

                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
                Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);

                // Act - render the component, then click the Search button
                var cut = RenderComponent<Devices>();
                cut.FindAll("button").First(b => b.TextContent.Trim() == "Search").Click();

                // Assert: the user's group ID was forwarded to GetDevicesSearchAsync
                Assert.IsNotNull(capturedGroupIds, "GetDevicesSearchAsync should have been called with the user's group IDs.");
                Assert.IsTrue(capturedGroupIds.Contains(userGroupId.ToString()),
                    "The user's group ID should be passed to GetDevicesSearchAsync to scope results to authorized tags.");

                // Assert: only the authorized device appears in the results
                Assert.IsTrue(cut.Markup.Contains("AuthorizedMake"),
                    "The authorized device Make should appear in the search results.");
                Assert.IsTrue(cut.Markup.Contains("AUTH-001"),
                    "The authorized device SerialNumber should appear in the search results.");

                // Assert: the unauthorized device is not shown
                Assert.IsFalse(cut.Markup.Contains("UnauthorizedMake"),
                    "The unauthorized device Make should NOT appear in the search results.");
                Assert.IsFalse(cut.Markup.Contains("UNAUTH-001"),
                    "The unauthorized device SerialNumber should NOT appear in the search results.");
            }
        }

        private void AddDefaultServices(string defaultId = "")
        {

            //      Create fake services
            List<DeviceTag> deviceTags = new List<DeviceTag>();
            DeviceTag deviceTag1 = new DeviceTag();
            deviceTag1.Name = "testName1";
            deviceTag1.Description = "testDescription1";
            deviceTags.Add(deviceTag1);
            DeviceTag deviceTag2 = new DeviceTag();
            deviceTag2.Name = "testName2";
            deviceTag2.Description = "testDescription2";
            deviceTags.Add(deviceTag2);
            var fakeDeviceTagDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceTagDBService()
            {
                GetDeviceTagsAsyncIEnumerableOfStringString =
                    (groupIds, name) => Task.FromResult(deviceTags)
            };


            DelegationStationShared.Models.Device device1 = new DelegationStationShared.Models.Device()
            {
                Make = "1",
                Model = "2",
                SerialNumber = "3",
                Status = DeviceStatus.Added,
                Tags = new List<string>() { deviceTag1.Id.ToString() }
            };
            List<DelegationStationShared.Models.Device> devices = new List<DelegationStationShared.Models.Device>();
            devices.Add(device1);
            var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
            {
                GetDevicesAsyncIEnumerableOfStringInt32Int32 = (a, c, d) =>
                    Task.FromResult(devices)
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
            Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
        }
    }
}