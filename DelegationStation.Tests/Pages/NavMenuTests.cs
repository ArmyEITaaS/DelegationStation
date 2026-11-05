using Bunit;
using DelegationStation.Pages;
using DelegationStation.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;
using DelegationStation.Interfaces;
using Microsoft.QualityTools.Testing.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using DelegationStation.Shared;

namespace DelegationStation.Tests.Pages
{
    [TestClass]
    public class NavMenuTests : BunitTestContext
    {
        [TestMethod]
        public void DisplayRolesMenuToAdmins()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                Guid userGroupId = Guid.NewGuid();
                var authContext = this.AddAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", defaultId.ToString()));
                authContext.SetPolicies("DelegationStationAdmin");

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

                // Add Dependent Services
                Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
                Services.AddSingleton<IHttpContextAccessor>(httpContext);
                Services.AddSingleton<DelegationStation.Services.HelpNotificationService>();
                Services.AddSingleton<IReleaseNotesService>(new TestReleaseNotesService());
                Services.AddDataProtection();
                Services.AddSingleton<Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage.ProtectedLocalStorage>();
                JSInterop.Mode = JSRuntimeMode.Loose;


                // Act
                var cut = Render<NavMenu>();

                // Assert
Assert.AreEqual(1, cut.FindAll("a[href='Roles']").Count, $"Role link should be rendered. Actual: {cut.Markup}");


            }
        }

        [TestMethod]
        public void DoNotDisplayRolesMenuToNonAdmins()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                Guid userGroupId = Guid.NewGuid();
                var authContext = this.AddAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));
                authContext.SetClaims(new System.Security.Claims.Claim("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", userGroupId.ToString()));


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

                // Add Dependent Services
                Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
                Services.AddSingleton<IHttpContextAccessor>(httpContext);
                Services.AddSingleton<DelegationStation.Services.HelpNotificationService>();
                Services.AddSingleton<IReleaseNotesService>(new TestReleaseNotesService());
                Services.AddDataProtection();
                Services.AddSingleton<Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage.ProtectedLocalStorage>();
                JSInterop.Mode = JSRuntimeMode.Loose;


                // Act
                var cut = Render<NavMenu>();

                // Assert
Assert.AreEqual(0, cut.FindAll("a[href='Roles']").Count, $"Menu should not display Roles link. Actual: {cut.Markup}");
            }
        }

        [TestMethod]
        public void DisplayUpdatesBadgeWhenUpdatesNotViewed()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));

                var myConfiguration = new Dictionary<string, string?>
                {
                    {"DefaultAdminGroupObjectId", defaultId.ToString()}
                };

                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(myConfiguration)
                    .Build();

                var httpContext = new HttpContextAccessor();
                httpContext.HttpContext = new DefaultHttpContext();

                Services.AddSingleton<IConfiguration>(configuration);
                Services.AddSingleton<IHttpContextAccessor>(httpContext);
                Services.AddSingleton<HelpNotificationService>();
                Services.AddSingleton<IReleaseNotesService>(new TestReleaseNotesService());
                Services.AddDataProtection();
                Services.AddSingleton<Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage.ProtectedLocalStorage>();
                JSInterop.Mode = JSRuntimeMode.Loose;

                // Act
                var cut = Render<NavMenu>();

                // Assert - badge should show because local storage has no viewed version
                Assert.AreEqual(1, cut.FindAll(".bg-danger.rounded-circle").Count, $"Updates badge should be displayed when updates have not been viewed. Actual: {cut.Markup}");
                Assert.AreEqual(1, cut.FindAll(".visually-hidden").Count, $"Accessible label for new updates should be present. Actual: {cut.Markup}");
            }
        }

        [TestMethod]
        public void HideUpdatesBadgeAfterUpdatesViewed()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                var authContext = this.AddAuthorization();
                authContext.SetAuthorized("TEST USER");
                authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));

                var myConfiguration = new Dictionary<string, string?>
                {
                    {"DefaultAdminGroupObjectId", defaultId.ToString()}
                };

                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(myConfiguration)
                    .Build();

                var httpContext = new HttpContextAccessor();
                httpContext.HttpContext = new DefaultHttpContext();

                var updatesNotification = new HelpNotificationService();

                Services.AddSingleton<IConfiguration>(configuration);
                Services.AddSingleton<IHttpContextAccessor>(httpContext);
                Services.AddSingleton(updatesNotification);
                Services.AddSingleton<IReleaseNotesService>(new TestReleaseNotesService());
                Services.AddDataProtection();
                Services.AddSingleton<Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage.ProtectedLocalStorage>();
                JSInterop.Mode = JSRuntimeMode.Loose;

                var cut = Render<NavMenu>();

                // Act - simulate the user viewing the Help page
                updatesNotification.MarkAsViewed();
                cut.WaitForState(() => cut.FindAll(".bg-danger.rounded-circle").Count == 0);

                // Assert - badge should be hidden after updates are marked as viewed
                Assert.AreEqual(0, cut.FindAll(".bg-danger.rounded-circle").Count, $"Updates badge should not be displayed after updates have been viewed. Actual: {cut.Markup}");
                Assert.AreEqual(0, cut.FindAll(".visually-hidden").Count, $"Accessible label for new updates should not be present after viewed. Actual: {cut.Markup}");
            }

        }

        private sealed class TestReleaseNotesService : IReleaseNotesService
        {
            public IReadOnlyList<ReleaseNote> ReleaseNotes { get; } = new List<ReleaseNote>
            {
                new ReleaseNote { Version = "test-version" }
            };

            public string CurrentVersion => ReleaseNotes[0].Version;
        }
    }
}
