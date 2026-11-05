using DelegationStation.Interfaces;
using DelegationStation.Pages;
using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.QualityTools.Testing.Fakes;
using System.Text.RegularExpressions;

namespace DelegationStation.Tests.Pages
{
    [TestClass]
    public class DevicesTests : BunitTestContext
    {
        [TestMethod]
        public void DevicesShouldRender()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                AddDefaultServices(defaultId.ToString());

                var authContext = this.AddAuthorization();
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
                device.Status = DeviceStatus.NonSyncing;
                device.Tags.Add(deviceTag.Name);
                devices.Add(device);

                var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
                {
                    GetDevicesAsyncIEnumerableOfStringDeviceInt32Int32 =
                        (groupIds,searchDevice,pageSize,currentPage) => Task.FromResult(devices),
                    // Stub for lazy loading: return a count matching the device list
                    GetDeviceSearchCountAsyncIEnumerableOfStringDevice =
                        (groupIds, searchDevice) => Task.FromResult(devices.Count)
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
                var cut = Render<Devices>();

                // Assert
                Assert.IsTrue(cut.Markup.Contains("testMake</td>"), $"testMake should be rendered in the table as a Make. \\nActual:\\n{cut.Markup}\"");
                Assert.IsTrue(cut.Markup.Contains("testModel</td>"), "testModel should be rendered in the table as a Model.");
                Assert.IsTrue(cut.Markup.Contains("1111</td>"), "1111 should be rendered in the table as a serialNumber.");
                Assert.IsTrue(cut.Markup.Contains("testHostname</td>"), "testHostname should be rendered in the table as a Hostname.");
                Assert.IsTrue(cut.Markup.Contains(">Non-Syncing</span></td>"), "Non-syncing devices should render with the Non-Syncing state label.");
                Assert.IsFalse(cut.Markup.Contains(">NonSyncing</span></td>"), "Non-syncing devices should render with the Non-Syncing state label.");
                Assert.IsFalse(cut.Markup.Contains(">Added</span></td>"), "Non-syncing devices should not render with the Added state label.");
            }
        }

        [TestMethod]
        public void UnauthorizedShouldNotRender()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                AddDefaultServices();

                var authContext = this.AddAuthorization();
                authContext.SetNotAuthorized();

                // Act
                var cut = Render<Devices>();

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

                var authContext = this.AddAuthorization();
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
                    GetDevicesAsyncIEnumerableOfStringDeviceInt32Int32 =
                        (groupIds, searchDevice, pageSize, currentPage) => Task.FromResult(new List<Device>()),
                    // Search stub: capture the groupIds passed and return only the authorized device
                    GetDevicesSearchAsyncIEnumerableOfStringDeviceInt32Int32 =
                        (groupIds, searchDevice, pageSize, currentPage) =>
                        {
                            capturedGroupIds = groupIds;
                            return Task.FromResult(new List<Device>() { authorizedDevice });
                        },
                        GetDeviceSearchCountAsyncIEnumerableOfStringDevice =
                            (groupIds, searchDevice) => Task.FromResult(1)
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
                var cut = Render<Devices>();
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

        [TestMethod]
        public void DevicesShouldShowPaginationControls()
        {
            using (ShimsContext.Create())
            {
                // Arrange – two pages of 10 devices, 15 total
                Guid defaultId = Guid.NewGuid();

                var authContext = this.AddAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                List<DeviceTag> deviceTags = new List<DeviceTag>();
                DeviceTag deviceTag = new DeviceTag();
                deviceTag.Name = "testTag";
                deviceTags.Add(deviceTag);

                var fakeDeviceTagDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceTagDBService()
                {
                    GetDeviceTagsAsyncIEnumerableOfStringString =
                        (groupIds, name) => Task.FromResult(deviceTags)
                };

                // First page: 10 devices
                List<Device> firstPageDevices = Enumerable.Range(1, 10)
                    .Select(i => new Device { Make = $"Make{i}", Model = "Model", SerialNumber = $"SN{i}" })
                    .ToList();

                var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
                {
                    GetDevicesAsyncIEnumerableOfStringDeviceInt32Int32 =
                        (groupIds, searchDevice, pageSize, page) => Task.FromResult(firstPageDevices),
                    // 15 total devices → 2 pages of 10
                    GetDeviceSearchCountAsyncIEnumerableOfStringDevice =
                        (groupIds, searchDevice) => Task.FromResult(15)
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

                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
                Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);

                // Act
                var cut = Render<Devices>();

                // Assert: pagination controls are rendered showing page 1 of 2
                Assert.IsTrue(cut.Markup.Contains("1 of 2"), $"Pagination should show '1 of 2'. Actual:\n{cut.Markup}");
                Assert.IsTrue(cut.Markup.Contains("aria-label=\"First\""), "First-page button should be rendered.");
                Assert.IsTrue(cut.Markup.Contains("aria-label=\"Last\""), "Last-page button should be rendered.");
                Assert.IsTrue(cut.Markup.Contains("aria-label=\"Next\""), "Next-page button should be rendered.");
                Assert.IsTrue(cut.Markup.Contains("aria-label=\"Previous\""), "Previous-page button should be rendered.");
            }
        }

        [TestMethod]
        public void DevicesShouldShowPageOneOfOneWhenNoDevices()
        {
            using (ShimsContext.Create())
            {
                // Arrange – zero devices
                Guid defaultId = Guid.NewGuid();

                var authContext = this.AddAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                List<DeviceTag> deviceTags = new List<DeviceTag>();
                var fakeDeviceTagDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceTagDBService()
                {
                    GetDeviceTagsAsyncIEnumerableOfStringString =
                        (groupIds, name) => Task.FromResult(deviceTags)
                };

                var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
                {
                    GetDevicesAsyncIEnumerableOfStringDeviceInt32Int32 =
                        (groupIds, searchDevice, pageSize, page) => Task.FromResult(new List<Device>()),
                    // No devices → count is 0
                    GetDeviceSearchCountAsyncIEnumerableOfStringDevice =
                        (groupIds, searchDevice) => Task.FromResult(0)
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

                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
                Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);

                // Act
                var cut = Render<Devices>();

                // Assert: no devices message is shown (no pagination row when count is 0)
                Assert.IsTrue(cut.Markup.Contains("No devices found."), $"Should show no-devices message. Actual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void DevicesShouldShowPaginationAfterSearch()
        {
            using (ShimsContext.Create())
            {
                // Arrange – search returns 15 devices → 2 pages of 10
                Guid defaultId = Guid.NewGuid();

                var authContext = this.AddAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                List<DeviceTag> deviceTags = new List<DeviceTag>();
                var fakeDeviceTagDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceTagDBService()
                {
                    GetDeviceTagsAsyncIEnumerableOfStringString = (groupIds, name) => Task.FromResult(deviceTags)
                };

                List<Device> firstPageDevices = Enumerable.Range(1, 10)
                    .Select(i => new Device { Make = "Dell", Model = $"Model{i}", SerialNumber = $"SN{i}" })
                    .ToList();

                var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
                {
                    // Initial load returns nothing
                    GetDevicesAsyncIEnumerableOfStringDeviceInt32Int32 =
                        (g, s, ps, p) => Task.FromResult(new List<Device>()),
                    //GetDeviceCountAsyncIEnumerableOfStringString =
                    //    (g, s) => Task.FromResult(0),
                    // Search returns 15 total; first page has 10 devices
                    GetDeviceSearchCountAsyncIEnumerableOfStringDevice =
                        (groupIds, searchDevice) => Task.FromResult(15),
                    GetDevicesSearchAsyncIEnumerableOfStringDeviceInt32Int32 =
                        (groupIds, searchDevice, pageSize, page) => Task.FromResult(firstPageDevices)
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

                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
                Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);

                // Act – render component and trigger Search
                var cut = Render<Devices>();
                var makeInput = cut.Find("input[placeholder='Make']");
                makeInput.Change("Dell");
                var searchButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Search");
                searchButton.Click();

                // Assert: pagination shows "1 of 2" after search with 15 results
                Assert.IsTrue(cut.Markup.Contains("1 of 2"), $"Pagination should show '1 of 2' after search. Actual:\n{cut.Markup}");
                Assert.IsTrue(cut.Markup.Contains("Dell"), "First-page search results should be visible.");
            }
        }

        [TestMethod]
        public void SearchByTagAsAdminShouldForwardAllMatchingTagIds()
        {
            using (ShimsContext.Create())
            {
                // Arrange – admin: user's group == DefaultAdminGroupObjectId, so all tags are accessible
                Guid defaultId = Guid.NewGuid();

                var authContext = this.AddAuthorization();
                authContext.SetAuthorized("ADMIN USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "ADMIN USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                // Admin sees every tag – two of them match "Corp"
                DeviceTag corpLaptops = new DeviceTag() { Name = "Corp-Laptops" };
                DeviceTag corpPhones = new DeviceTag() { Name = "Corp-Phones" };
                DeviceTag kiosks = new DeviceTag() { Name = "Kiosks" };
                List<DeviceTag> allTags = new List<DeviceTag>() { corpLaptops, corpPhones, kiosks };

                var fakeDeviceTagDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceTagDBService()
                {
                    GetDeviceTagsAsyncIEnumerableOfStringString = (groupIds, name) => Task.FromResult(allTags)
                };

                List<string>? capturedTags = null;
                var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
                {
                    GetDevicesAsyncIEnumerableOfStringDeviceInt32Int32 =
                        (g, s, ps, p) => Task.FromResult(new List<Device>()),
                    GetDeviceSearchCountAsyncIEnumerableOfStringDevice =
                        (g, s) => Task.FromResult(0),
                    GetDevicesSearchAsyncIEnumerableOfStringDeviceInt32Int32 =
                        (groupIds, searchDevice, pageSize, page) =>
                        {
                            capturedTags = searchDevice.Tags?.ToList();
                            return Task.FromResult(new List<Device>());
                        }
                };

                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { { "DefaultAdminGroupObjectId", defaultId.ToString() } })
                    .Build();

                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
                Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);

                // Act – type into the Tag search input and click Search
                var cut = Render<Devices>();
                cut.Find("input[placeholder='Tag']").Change("Corp");
                cut.FindAll("button").First(b => b.TextContent.Trim() == "Search").Click();

                // Assert – both admin-visible tags matching "Corp" are forwarded to the search
                Assert.IsNotNull(capturedTags, "GetDevicesSearchAsync should have been invoked.");
                Assert.AreEqual(2, capturedTags!.Count, "Admin should forward all matching tag IDs.");
                CollectionAssert.Contains(capturedTags, corpLaptops.Id.ToString());
                CollectionAssert.Contains(capturedTags, corpPhones.Id.ToString());
                CollectionAssert.DoesNotContain(capturedTags, kiosks.Id.ToString());
            }
        }

        [TestMethod]
        public void SearchByTagAsNonAdminShouldForwardOnlyAuthorizedMatchingTagIds()
        {
            using (ShimsContext.Create())
            {
                // Arrange – non-admin: user's group != DefaultAdminGroupObjectId
                Guid userGroupId = Guid.NewGuid();
                Guid defaultId = Guid.NewGuid();

                var authContext = this.AddAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", userGroupId.ToString()));

                // Tag DB service only returns tags the non-admin user has access to
                DeviceTag corpLaptops = new DeviceTag() { Name = "Corp-Laptops" };
                List<DeviceTag> accessibleTags = new List<DeviceTag>() { corpLaptops };

                var fakeDeviceTagDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceTagDBService()
                {
                    GetDeviceTagsAsyncIEnumerableOfStringString = (groupIds, name) => Task.FromResult(accessibleTags)
                };

                List<string>? capturedTags = null;
                var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
                {
                    GetDevicesAsyncIEnumerableOfStringDeviceInt32Int32 =
                        (g, s, ps, p) => Task.FromResult(new List<Device>()),
                    GetDeviceSearchCountAsyncIEnumerableOfStringDevice =
                        (g, s) => Task.FromResult(0),
                    GetDevicesSearchAsyncIEnumerableOfStringDeviceInt32Int32 =
                        (groupIds, searchDevice, pageSize, page) =>
                        {
                            capturedTags = searchDevice.Tags?.ToList();
                            return Task.FromResult(new List<Device>());
                        }
                };

                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { { "DefaultAdminGroupObjectId", defaultId.ToString() } })
                    .Build();

                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
                Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);

                // Act – search for "Corp"; the non-admin's inaccessible "Corp-Phones" tag isn't in deviceTags
                var cut = Render<Devices>();
                cut.Find("input[placeholder='Tag']").Change("Corp");
                cut.FindAll("button").First(b => b.TextContent.Trim() == "Search").Click();

                // Assert – only the authorized matching tag ID is forwarded
                Assert.IsNotNull(capturedTags, "GetDevicesSearchAsync should have been invoked.");
                Assert.AreEqual(1, capturedTags!.Count, "Non-admin should only forward authorized matching tag IDs.");
                CollectionAssert.Contains(capturedTags, corpLaptops.Id.ToString());
            }
        }

        [TestMethod]
        public void SearchByStatusShouldForwardSelectedStatusToSearch()
        {
            using (ShimsContext.Create())
            {
                // Arrange – admin: user's group == DefaultAdminGroupObjectId, so all tags are accessible
                Guid defaultId = Guid.NewGuid();

                var authContext = this.AddAuthorization();
                authContext.SetAuthorized("ADMIN USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "ADMIN USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                var fakeDeviceTagDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceTagDBService()
                {
                    GetDeviceTagsAsyncIEnumerableOfStringString = (groupIds, name) => Task.FromResult(new List<DeviceTag>())
                };

                DeviceStatus? capturedStatus = null;
                var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
                {
                    GetDevicesAsyncIEnumerableOfStringDeviceInt32Int32 =
                        (g, s, ps, p) => Task.FromResult(new List<Device>()),
                    GetDeviceSearchCountAsyncIEnumerableOfStringDevice =
                        (g, s) =>
                        {
                            capturedStatus = s.Status;
                            return Task.FromResult(0);
                        },
                    GetDevicesSearchAsyncIEnumerableOfStringDeviceInt32Int32 =
                        (groupIds, searchDevice, pageSize, page) =>
                        {
                            capturedStatus = searchDevice.Status;
                            return Task.FromResult(new List<Device>());
                        }
                };

                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { { "DefaultAdminGroupObjectId", defaultId.ToString() } })
                    .Build();

                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
                Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);

                // Act – select a state in the State dropdown and click Search
                var cut = Render<Devices>();
                cut.Find("#State").Change(DeviceStatus.Synced.ToString());
                cut.FindAll("button").First(b => b.TextContent.Trim() == "Search").Click();

                // Assert – the selected status is forwarded to the search service
                Assert.IsNotNull(capturedStatus, "GetDevicesSearchAsync should have been invoked with a selected status.");
                Assert.AreEqual(DeviceStatus.Synced, capturedStatus, "The selected device state should be forwarded to the search.");
            }
        }

        [TestMethod]
        public void SearchWithoutStatusShouldForwardNullStatusToSearch()
        {
            using (ShimsContext.Create())
            {
                // Arrange – admin with no state selected in the dropdown
                Guid defaultId = Guid.NewGuid();

                var authContext = this.AddAuthorization();
                authContext.SetAuthorized("ADMIN USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "ADMIN USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                var fakeDeviceTagDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceTagDBService()
                {
                    GetDeviceTagsAsyncIEnumerableOfStringString = (groupIds, name) => Task.FromResult(new List<DeviceTag>())
                };

                bool searchInvoked = false;
                DeviceStatus? capturedStatus = null;
                var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
                {
                    GetDevicesAsyncIEnumerableOfStringDeviceInt32Int32 =
                        (g, s, ps, p) => Task.FromResult(new List<Device>()),
                    GetDeviceSearchCountAsyncIEnumerableOfStringDevice =
                        (g, s) => Task.FromResult(0),
                    GetDevicesSearchAsyncIEnumerableOfStringDeviceInt32Int32 =
                        (groupIds, searchDevice, pageSize, page) =>
                        {
                            searchInvoked = true;
                            capturedStatus = searchDevice.Status;
                            return Task.FromResult(new List<Device>());
                        }
                };

                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { { "DefaultAdminGroupObjectId", defaultId.ToString() } })
                    .Build();

                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
                Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);

                // Act – click Search without selecting a state
                var cut = Render<Devices>();
                cut.FindAll("button").First(b => b.TextContent.Trim() == "Search").Click();

                // Assert – no status filter is forwarded when none is selected
                Assert.IsTrue(searchInvoked, "GetDevicesSearchAsync should have been invoked.");
                Assert.IsNull(capturedStatus, "No status filter should be forwarded when no state is selected.");
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
                GetDevicesAsyncIEnumerableOfStringDeviceInt32Int32 = (a, b, c, d) =>
                    Task.FromResult(devices),
                // Stub for lazy loading: return a count matching the device list
                GetDeviceSearchCountAsyncIEnumerableOfStringDevice =
                (groupIds, searchDevice) => Task.FromResult(devices.Count)
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
