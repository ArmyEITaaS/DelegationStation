using DelegationStation.Shared;

namespace DelegationStation.Tests.Pages
{
    [TestClass]
    public class LoginDisplayTests : Bunit.TestContext
    {
        [TestMethod]
        public void UnauthorizedShouldNotRender()
        {
            // Arrange
            var authContext = this.AddTestAuthorization();
            authContext.SetNotAuthorized();

            // Act
            var cut = RenderComponent<LoginDisplay>();

            // Assert
            cut.MarkupMatches(@"<a href=""MicrosoftIdentity/Account/SignIn"">Log in</a>");
        }

        [TestMethod]
        public void SuccessfulLoginShouldShowSignOutLink()
        {
            // Arrange
            var authContext = this.AddTestAuthorization();
            authContext.SetAuthorized("TEST USER");
            authContext.SetClaims(new System.Security.Claims.Claim("name", "TEST USER"));

            // Act
            var cut = RenderComponent<LoginDisplay>();

            // Assert            
            cut.Markup.Contains(@"<a href=""MicrosoftIdentity/Account/SignOut"">Log out</a>");
        }

        [TestMethod]
        public void SuccessfulLoginShouldShowUserName()
        {
            // Arrange
            var authContext = this.AddTestAuthorization();
            string userName = "TEST USER";
            authContext.SetAuthorized(userName);
            authContext.SetClaims(new System.Security.Claims.Claim("name", userName));

            // Act
            var cut = RenderComponent<LoginDisplay>();

            // Assert
            bool test = cut.Markup.Contains(userName);
            Assert.IsTrue(test, $"Markup should contain {userName}");

        }
    }
}
