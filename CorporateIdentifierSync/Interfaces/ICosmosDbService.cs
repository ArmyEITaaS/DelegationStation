using DelegationStationShared.Models;

namespace CorporateIdentifierSync.Interfaces
{
    public interface ICosmosDbService
    {
        Task<List<Device>> GetAddedDevices(int batchSize);
        Task<List<Device>> GetAddedDevicesNotSyncing(List<string> tagIds, int batchSize);
        Task<List<Device>> GetAddedDevicesToSync(List<string> tagIds, int batchSize);

        Task<List<Device>> GetDevicesMarkedForDeletion();

        Task UpdateDevice(Device device);

        Task DeleteDevice(Device device);
        Task<Device?> GetDevice(Guid id, string partitionKey);

        Task<List<Device>> GetDevicesSyncedBefore(DateTime date);
        Task<List<Device>> GetSyncedDevicesSyncedBefore(DateTime date);

        Task<DeviceTag> GetDeviceTag(string id);

        Task<List<string>> GetSyncingDeviceTags();
        Task<List<string>> GetNonSyncingDeviceTags();

        Task<CorpIDCounter> GetCorpIDCounter();
        Task<bool> TrySetCorpIDCounter(CorpIDCounter counter, string etag);

        Task<List<Device>> GetSyncedDevicesInTags(List<string> tagIds, int batchSize);
        Task<List<Device>> GetNotSyncingDevicesInTags(List<string> tagsWithSyncEnabled, int batchSize);

        Task<int> GetSyncedDeviceCountAsync();
    }
}
