using DelegationStationShared.Models;

namespace DelegationStation.Interfaces
{
    public interface IDeviceDBService
    {
        Task<Device> AddOrUpdateDeviceAsync(Device device);
        Task<List<Device>> GetDevicesSearchAsync(IEnumerable<string> groupIds, Device device, int pageSize = 10, int page = 0);
        Task<List<Device>> GetDevicesAsync(IEnumerable<string> groupIds, Device device, int pageSize = 10, int page = 0);
        /// <summary>Returns the total number of devices matching the given per-field search criteria.</summary>
        Task<int> GetDeviceSearchCountAsync(IEnumerable<string> groupIds, Device device);
        Task<Device?> GetDeviceAsync(string make, string model, string serialNumber);
        Task<List<Device>> GetDevicesByTagAsync(string tagId);
        Task MarkDeviceToDeleteAsync(Device device);
    }
}