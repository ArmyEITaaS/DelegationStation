using DelegationStation.Services;
using DelegationStation.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace DelegationStation.Tests.Pages
{
    [TestClass]
    public class MaintenanceBannerTests : BunitTestContext
    {
        [TestMethod]
        public void NoBannerFilePresentShouldNotRenderAnything()
        {
            // Arrange
            Services.AddSingleton<IMaintenanceBannerService>(new TestMaintenanceBannerService(null));

            // Act
            var cut = Render<MaintenanceBanner>();

            // Assert
            Assert.AreEqual(string.Empty, cut.Markup.Trim(), $"No markup should render when there is no banner message. Actual: {cut.Markup}");
        }

        [TestMethod]
        public void ShortMessageShouldRenderWithoutScrolling()
        {
            // Arrange
            string message = "Scheduled maintenance tonight.";
            Services.AddSingleton<IMaintenanceBannerService>(new TestMaintenanceBannerService(new MaintenanceBannerContent(message, MaintenanceBannerTheme.Amber)));

            // Act
            var cut = Render<MaintenanceBanner>();

            // Assert
            Assert.IsTrue(cut.Markup.Contains(message), $"Markup should contain the banner message. Actual: {cut.Markup}");
            Assert.AreEqual(0, cut.FindAll(".maintenance-banner-track").Count, $"Short messages should not scroll. Actual: {cut.Markup}");
            Assert.AreEqual(1, cut.FindAll(".maintenance-banner-amber").Count, $"Default theme class should apply when no theme is specified. Actual: {cut.Markup}");
        }

        [TestMethod]
        public void LongMessageShouldScroll()
        {
            // Arrange
            string message = new string('a', 200);
            Services.AddSingleton<IMaintenanceBannerService>(new TestMaintenanceBannerService(new MaintenanceBannerContent(message, MaintenanceBannerTheme.Amber)));

            // Act
            var cut = Render<MaintenanceBanner>();

            // Assert
            Assert.AreEqual(1, cut.FindAll(".maintenance-banner-track").Count, $"Long messages should scroll. Actual: {cut.Markup}");
        }

        [TestMethod]
        [DataRow(MaintenanceBannerTheme.Red, "maintenance-banner-red")]
        [DataRow(MaintenanceBannerTheme.Green, "maintenance-banner-green")]
        [DataRow(MaintenanceBannerTheme.Blue, "maintenance-banner-blue")]
        public void NamedThemeShouldApplyMatchingCssClass(MaintenanceBannerTheme theme, string expectedClass)
        {
            // Arrange
            string message = "Scheduled maintenance tonight.";
            Services.AddSingleton<IMaintenanceBannerService>(new TestMaintenanceBannerService(new MaintenanceBannerContent(message, theme)));

            // Act
            var cut = Render<MaintenanceBanner>();

            // Assert
            Assert.AreEqual(1, cut.FindAll($".{expectedClass}").Count, $"Theme class {expectedClass} should be applied. Actual: {cut.Markup}");
        }

        [TestMethod]
        [DataRow("red", MaintenanceBannerTheme.Red)]
        [DataRow("RED", MaintenanceBannerTheme.Red)]
        [DataRow("green", MaintenanceBannerTheme.Green)]
        [DataRow("blue", MaintenanceBannerTheme.Blue)]
        public void ParseShouldRecognizeFirstLineThemeAndSeparateMessage(string themeLine, MaintenanceBannerTheme expectedTheme)
        {
            // Act
            var banner = MaintenanceBannerService.Parse(new[] { themeLine, "Scheduled maintenance tonight." });

            // Assert
            Assert.IsNotNull(banner);
            Assert.AreEqual(expectedTheme, banner!.Theme);
            Assert.AreEqual("Scheduled maintenance tonight.", banner.Message);
        }

        [TestMethod]
        public void ParseShouldTreatWholeFileAsMessageAndDefaultToAmberWhenFirstLineIsNotAKnownTheme()
        {
            // Act
            var banner = MaintenanceBannerService.Parse(new[] { "Scheduled maintenance tonight at 9pm." });

            // Assert
            Assert.IsNotNull(banner);
            Assert.AreEqual(MaintenanceBannerTheme.Amber, banner!.Theme);
            Assert.AreEqual("Scheduled maintenance tonight at 9pm.", banner.Message);
        }

        private sealed class TestMaintenanceBannerService : IMaintenanceBannerService
        {
            private readonly MaintenanceBannerContent? _banner;

            public TestMaintenanceBannerService(MaintenanceBannerContent? banner)
            {
                _banner = banner;
            }

            public MaintenanceBannerContent? GetBanner() => _banner;
        }
    }
}
