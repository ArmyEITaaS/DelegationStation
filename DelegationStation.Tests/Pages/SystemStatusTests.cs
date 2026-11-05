using DelegationStation.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.QualityTools.Testing.Fakes;
using Microsoft.Extensions.Configuration;
using DelegationStation.Interfaces;

namespace DelegationStation.Tests.Pages
{
    [TestClass]
    public class SystemStatusTests : Bunit.TestContext
    {
        private static DelegationStation.Interfaces.Fakes.StubIDeviceTagDBService CreateTagService(List<DeviceTag> deviceTags)
        {
            return new DelegationStation.Interfaces.Fakes.StubIDeviceTagDBService()
            {
                CurrentSearchGet = () => new DelegationStation.Services.DeviceTagSearch()
                    { pageNumber = 1, pageSize = 10, name = string.Empty },
                GetDeviceTagsAsyncIEnumerableOfStringString =
                    (groupIds, name) => Task.FromResult(deviceTags),
                GetDeviceTagCountAsyncIEnumerableOfStringString =
                    (groupIds, name) => Task.FromResult(deviceTags.Count),
                GetDeviceTagsByPageAsyncIEnumerableOfStringInt32Int32String =
                    (groupIds, pageNumber, pageSize, name) => Task.FromResult(deviceTags)
            };
        }

        private static IConfiguration CreateConfiguration(Guid defaultId, int? maxCorpIDsAllowed = null)
        {
            var myConfiguration = new Dictionary<string, string?>
            {
                {"DefaultAdminGroupObjectId", defaultId.ToString()},
                {"MAX_CORPIDS_ALLOWED", maxCorpIDsAllowed?.ToString()}
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(myConfiguration)
                .Build();
        }

        [TestMethod]
        public void CorporateIdentifierStatusShouldRenderCounterValues()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                var fakeDeviceTagDBService = CreateTagService(new List<DeviceTag>());
                var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
                {
                    GetDevicesByTagAsyncString = (tagId) => Task.FromResult(new List<Device>())
                };
                var fakeCorpIdDBService = new DelegationStation.Interfaces.Fakes.StubICorpIdDBService()
                {
                    GetCorpIDCounterAsync = () => Task.FromResult<CorpIDCounter?>(new CorpIDCounter(2500))
                };

                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
                Services.AddSingleton<ICorpIdDBService>(fakeCorpIdDBService);
                Services.AddSingleton<IConfiguration>(CreateConfiguration(defaultId, 10000));

                // Act
                var cut = Render<SystemStatus>();

                // Assert
                Assert.IsTrue(cut.Markup.Contains("10000"), $"Max allowed corporate identifiers should be rendered.\nActual:\n{cut.Markup}");
                Assert.IsTrue(cut.Markup.Contains("2500"), "Current corporate identifier count should be rendered.");
                Assert.IsTrue(cut.Markup.Contains("25%"), "Utilization percentage should be rendered.");
                Assert.IsTrue(cut.Markup.Contains("text-success"), "Utilization below 90% should use the success style.");
            }
        }

        [TestMethod]
        public void CorporateIdentifierStatusShouldShowErrorWhenCounterMissing()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                var fakeDeviceTagDBService = CreateTagService(new List<DeviceTag>());
                var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
                {
                    GetDevicesByTagAsyncString = (tagId) => Task.FromResult(new List<Device>())
                };
                var fakeCorpIdDBService = new DelegationStation.Interfaces.Fakes.StubICorpIdDBService()
                {
                    GetCorpIDCounterAsync = () => Task.FromResult<CorpIDCounter?>(null)
                };

                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
                Services.AddSingleton<ICorpIdDBService>(fakeCorpIdDBService);
                Services.AddSingleton<IConfiguration>(CreateConfiguration(defaultId));

                // Act
                var cut = Render<SystemStatus>();

                // Assert
                Assert.IsTrue(cut.Markup.Contains("Corporate Identifier counter was not found in the database."),
                    $"An error message should be shown when the counter is missing.\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void CorporateIdentifierStatusShouldShowErrorWhenCounterLoadFails()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                var fakeDeviceTagDBService = CreateTagService(new List<DeviceTag>());
                var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
                {
                    GetDevicesByTagAsyncString = (tagId) => Task.FromResult(new List<Device>())
                };
                var fakeCorpIdDBService = new DelegationStation.Interfaces.Fakes.StubICorpIdDBService()
                {
                    GetCorpIDCounterAsync = () => throw new Exception("Database unavailable")
                };

                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
                Services.AddSingleton<ICorpIdDBService>(fakeCorpIdDBService);
                Services.AddSingleton<IConfiguration>(CreateConfiguration(defaultId));

                // Act
                var cut = Render<SystemStatus>();

                // Assert
                Assert.IsTrue(cut.Markup.Contains("Failed to load Corporate Identifier status."),
                    $"An error message should be shown when loading the counter throws.\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void UtilizationShouldUseDangerStyleWhenAtOrAboveLimit()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                var fakeDeviceTagDBService = CreateTagService(new List<DeviceTag>());
                var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
                {
                    GetDevicesByTagAsyncString = (tagId) => Task.FromResult(new List<Device>())
                };
                var fakeCorpIdDBService = new DelegationStation.Interfaces.Fakes.StubICorpIdDBService()
                {
                    GetCorpIDCounterAsync = () => Task.FromResult<CorpIDCounter?>(new CorpIDCounter(150))
                };

                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
                Services.AddSingleton<ICorpIdDBService>(fakeCorpIdDBService);
                Services.AddSingleton<IConfiguration>(CreateConfiguration(defaultId, 100));

                // Act
                var cut = Render<SystemStatus>();

                // Assert
                Assert.IsTrue(cut.Markup.Contains("text-danger"),
                    $"Utilization at or above 100% should use the danger style.\nActual:\n{cut.Markup}");
                // The count is capped at the max allowed for display purposes.
                Assert.IsTrue(cut.Markup.Contains("100%"), "Utilization should be capped at 100%.");
            }
        }

        [TestMethod]
        public void TagsTableShouldRenderTagsWithSyncedDeviceCounts()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                DeviceTag deviceTag = new DeviceTag() { Name = "testTag" };
                var deviceTags = new List<DeviceTag> { deviceTag };
                var fakeDeviceTagDBService = CreateTagService(deviceTags);

                var devices = new List<Device>
                {
                    new Device() { Status = DelegationStationShared.Enums.DeviceStatus.Synced },
                    new Device() { Status = DelegationStationShared.Enums.DeviceStatus.Synced },
                    new Device() { Status = DelegationStationShared.Enums.DeviceStatus.Added }
                };
                var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
                {
                    GetDevicesByTagAsyncString = (tagId) => Task.FromResult(devices)
                };
                var fakeCorpIdDBService = new DelegationStation.Interfaces.Fakes.StubICorpIdDBService()
                {
                    GetCorpIDCounterAsync = () => Task.FromResult<CorpIDCounter?>(new CorpIDCounter(0))
                };

                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
                Services.AddSingleton<ICorpIdDBService>(fakeCorpIdDBService);
                Services.AddSingleton<IConfiguration>(CreateConfiguration(defaultId, 10000));

                // Act
                var cut = Render<SystemStatus>();

                // Assert
                Assert.IsTrue(cut.Markup.Contains("testTag"), $"Tag name should be rendered.\nActual:\n{cut.Markup}");
                // Only the two Synced devices should be counted.
                Assert.IsTrue(cut.Markup.Contains("<td>2</td>"), "Only synced devices should be counted.");
            }
        }

        [TestMethod]
        public void TagsTableShouldShowNoTagsMessageWhenEmpty()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                var fakeDeviceTagDBService = CreateTagService(new List<DeviceTag>());
                var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
                {
                    GetDevicesByTagAsyncString = (tagId) => Task.FromResult(new List<Device>())
                };
                var fakeCorpIdDBService = new DelegationStation.Interfaces.Fakes.StubICorpIdDBService()
                {
                    GetCorpIDCounterAsync = () => Task.FromResult<CorpIDCounter?>(new CorpIDCounter(0))
                };

                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
                Services.AddSingleton<ICorpIdDBService>(fakeCorpIdDBService);
                Services.AddSingleton<IConfiguration>(CreateConfiguration(defaultId));

                // Act
                var cut = Render<SystemStatus>();

                // Assert
                Assert.IsTrue(cut.Markup.Contains("No tags found."),
                    $"A no tags message should be shown when there are no tags.\nActual:\n{cut.Markup}");
            }
        }

        [TestMethod]
        public void TagsTableShouldShowErrorWhenDeviceCountRetrievalFails()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));

                DeviceTag deviceTag = new DeviceTag() { Name = "testTag" };
                var fakeDeviceTagDBService = CreateTagService(new List<DeviceTag> { deviceTag });

                var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
                {
                    GetDevicesByTagAsyncString = (tagId) => throw new Exception("Device lookup failed")
                };
                var fakeCorpIdDBService = new DelegationStation.Interfaces.Fakes.StubICorpIdDBService()
                {
                    GetCorpIDCounterAsync = () => Task.FromResult<CorpIDCounter?>(new CorpIDCounter(0))
                };

                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
                Services.AddSingleton<ICorpIdDBService>(fakeCorpIdDBService);
                Services.AddSingleton<IConfiguration>(CreateConfiguration(defaultId));

                // Act
                var cut = Render<SystemStatus>();

                // Assert
                Assert.IsTrue(cut.Markup.Contains("Unable to retrieve device count"),
                    $"A failed device count should be shown per tag.\nActual:\n{cut.Markup}");
                Assert.IsTrue(cut.Markup.Contains("N/A"), "Utilization should be N/A when the device count fails.");
            }
        }

        [TestMethod]
        public void TagsTableShouldNotRenderWhenNotAuthorized()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddAuthorization();
                authContext.SetNotAuthorized();

                var fakeDeviceTagDBService = CreateTagService(new List<DeviceTag>());
                var fakeDeviceDBService = new DelegationStation.Interfaces.Fakes.StubIDeviceDBService()
                {
                    GetDevicesByTagAsyncString = (tagId) => Task.FromResult(new List<Device>())
                };
                var fakeCorpIdDBService = new DelegationStation.Interfaces.Fakes.StubICorpIdDBService()
                {
                    GetCorpIDCounterAsync = () => Task.FromResult<CorpIDCounter?>(new CorpIDCounter(0))
                };

                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<IDeviceDBService>(fakeDeviceDBService);
                Services.AddSingleton<ICorpIdDBService>(fakeCorpIdDBService);
                Services.AddSingleton<IConfiguration>(CreateConfiguration(defaultId));

                // Act
                var cut = Render<SystemStatus>();

                // Assert
                Assert.IsTrue(cut.Markup.Contains("Not Authorized"),
                    $"An unauthorized user should see the not authorized message.\nActual:\n{cut.Markup}");
                Assert.IsFalse(cut.Markup.Contains("Number of Devices"),
                    "The tags table should not render for an unauthorized user.");
            }
        }
    }
}
