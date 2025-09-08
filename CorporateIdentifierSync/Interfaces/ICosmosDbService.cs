using DelegationStationShared.Models;

namespace CorporateIdentifierSync.Interfaces
{
    public interface ICosmosDbService
    {
        Task<List<Device>> GetAddedDevices(int batchSize);

        Task<List<Device>> GetDevicesMarkedForDeletion();

        Task UpdateDevice(Device device);

        Task DeleteDevice(Device device);

        Task<List<Device>> GetDevicesSyncedBefore(DateTime date);

        Task<DeviceTag> GetDeviceTag(string id);

        Task<List<string>> GetSyncEnabledDeviceTags();


    }
}
