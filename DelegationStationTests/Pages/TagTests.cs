using DelegationStation.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.QualityTools.Testing.Fakes;
using Microsoft.Extensions.Configuration;
using DelegationStation.Interfaces;

namespace DelegationStationTests.Pages
{
    [TestClass]
    public class TagTests : Bunit.TestContext
    {
        [TestMethod]
        public void TagsShouldRender()
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
                        (groupIds, name) => Task.FromResult(deviceTags),
                    GetDeviceTagCountAsyncIEnumerableOfStringString =
                        (groupIds, name) => Task.FromResult(2),
                    GetDeviceTagsByPageAsyncIEnumerableOfStringInt32Int32String =
                        (groupIds, pageNumber, pageSize, name) => Task.FromResult(deviceTags)
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


                //      Add Dependent Services
                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);


                // Act
                var cut = RenderComponent<Tags>();

                // Assert
                Assert.IsTrue(cut.Markup.Contains("testName1</td>"), $"Tag1 name should be rendered in the table. \\nActual:\\n{cut.Markup}\"");
                Assert.IsTrue(cut.Markup.Contains("testDescription1</td>"), "Tag1 description should be rendered in the table.");
                Assert.IsTrue(cut.Markup.Contains("testName2</td>"), "Tag2 name should be rendered in the table.");
                Assert.IsTrue(cut.Markup.Contains("testDescription2</td>"), "Tag2 description should be rendered in the table.");
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
                        (groupIds, name) => Task.FromResult(deviceTags),
                    GetDeviceTagCountAsyncIEnumerableOfStringString =
                        (groupIds, name) => Task.FromResult(2),
                    GetDeviceTagsByPageAsyncIEnumerableOfStringInt32Int32String =
                        (groupIds, pageNumber, pageSize, name) => Task.FromResult(deviceTags)
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

                //      Add Dependent Services
                Services.AddSingleton<IDeviceTagDBService>(fakeDeviceTagDBService);
                Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);

                // Act
                var cut = RenderComponent<Tags>();

                // Assert
                cut.MarkupMatches(@"
<h3>Tags</h3>
Not Authorized to create Tags
<p>Not Authorized</p>");
            }
        }
    }
}