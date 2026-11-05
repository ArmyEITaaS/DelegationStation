using DelegationStation.Services;
using Microsoft.AspNetCore.Components;

namespace DelegationStation.Shared
{
    public partial class MaintenanceBanner : IDisposable
    {
        /// <summary>
        /// Messages longer than this many characters scroll instead of wrapping,
        /// so a short notice stays static while a longer one remains readable.
        /// </summary>
        private const int ScrollThreshold = 80;

        /// <summary>
        /// How often to re-check for the banner file appearing, changing, or
        /// being removed, so operators don't need to restart the app or wait
        /// for users to reload the page.
        /// </summary>
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

        [Inject]
        private IMaintenanceBannerService MaintenanceBannerService { get; set; } = default!;

        private Timer? _pollTimer;

        private string? Message { get; set; }

        private MaintenanceBannerTheme Theme { get; set; } = MaintenanceBannerTheme.Amber;

        private string ThemeCssClass => $"maintenance-banner-{Theme.ToString().ToLowerInvariant()}";

        private bool IsScrolling => !string.IsNullOrEmpty(Message) && Message.Length > ScrollThreshold;

        /// <summary>
        /// Slower scroll for longer messages so the reading speed stays roughly
        /// constant, with a floor so short-but-over-threshold messages aren't rushed.
        /// </summary>
        private double ScrollDurationSeconds => Math.Max(15, (Message?.Length ?? 0) / 8.0);

        protected override void OnInitialized()
        {
            Apply(MaintenanceBannerService.GetBanner());
            _pollTimer = new Timer(_ => Poll(), null, PollInterval, PollInterval);
        }

        private void Poll()
        {
            var current = MaintenanceBannerService.GetBanner();
            if (current?.Message == Message && current?.Theme == Theme)
            {
                return;
            }

            Apply(current);
            InvokeAsync(StateHasChanged);
        }

        private void Apply(MaintenanceBannerContent? banner)
        {
            Message = banner?.Message;
            Theme = banner?.Theme ?? MaintenanceBannerTheme.Amber;
        }

        public void Dispose()
        {
            _pollTimer?.Dispose();
        }
    }
}
