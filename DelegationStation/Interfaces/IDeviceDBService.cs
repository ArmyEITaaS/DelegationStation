using DelegationStationShared.Models;

namespace DelegationStation.Interfaces
{
    public interface IDeviceDBService
    {
        Task<Device> AddOrUpdateDeviceAsync(Device device);
        Task<List<Device>> GetDevicesAsync(IEnumerable<string> groupIds);
        Task<List<Device>> GetDevicesSearchAsync(string make, string model, string serialNumber, int? osID, string preferredHostname);
        Task<List<Device>> GetDevicesAsync(IEnumerable<string> groupIds, string search, int pageSize = 10, int page = 0);
        Task<Device?> GetDeviceAsync(string make, string model, string serialNumber);
        Task<List<Device>> GetDevicesByTagAsync(string tagId);
        Task MarkDeviceToDeleteAsync(Device device);
    }
}