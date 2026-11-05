namespace DelegationStation.Services
{
    public class HelpNotificationService
    {
        public event Action? OnUpdatesViewed;

        public void MarkAsViewed()
        {
            OnUpdatesViewed?.Invoke();
        }
    }
}
