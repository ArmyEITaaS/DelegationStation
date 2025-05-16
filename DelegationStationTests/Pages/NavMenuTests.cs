using DelegationStation.Pages;
using Microsoft.Extensions.DependencyInjection;
using DelegationStation.Interfaces;
using Microsoft.QualityTools.Testing.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using DelegationStation.Shared;

namespace DelegationStationTests.Pages
{
    [TestClass]
    public class NavMenuTests : Bunit.TestContext
    {
        [TestMethod]
        public void DisplayRolesMenuToAdmins()
        {
            using (ShimsContext.Create())
            {
                // Arrange
                Guid defaultId = Guid.NewGuid();
                Guid userGroupId = Guid.NewGuid();
                var authContext = this.AddTestAuthorization();
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


                // Act
                var cut = RenderComponent<NavMenu>();

                // Assert
                Assert.IsTrue(cut.Markup.Contains("<a href=\"Roles\" class=\"nav-link\"><span class=\"oi oi-shield\" aria-hidden=\"true\" b-l9c7g71qbx></span> Roles\r\n            </a></div></nav></div>"), $"Role link should be rendered. \\nActual:\\n{cut.Markup}\"");


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
                var authContext = this.AddTestAuthorization();
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


                // Act
                var cut = RenderComponent<NavMenu>();

                // Assert
                Assert.IsFalse(cut.Markup.Contains("Roles"), $"Menu should not dispaly Roles link. \\nActual:\\n{cut.Markup}\"");
            }
        }
    }
}
