using System.Threading.Tasks;

namespace UpdateDevices.Interfaces
{
    public interface IGraphBetaService
    {
        Task<bool> SetDeviceName(string managedDeviceID, string newHostName);
    }
}
