using ManagedDevice = Microsoft.Graph.Models.ManagedDevice;
using UpdateDevices.Models;
using System.Threading.Tasks;
using DelegationStationShared.Models;
using System.Collections.Generic;
using System;

namespace UpdateDevices.Interfaces
{
    public interface ICosmosDbService
    {
        Task<FunctionSettings> GetFunctionSettings();
        Task UpdateFunctionSettings(DateTime thisRun);

        Task<Device> GetDevice(string make, string model, string serialNumber);

        Task<DeviceTag> GetDeviceTag(string tagId);

        Task AddOrUpdateStraggler(ManagedDevice managedDevice);
        Task<Straggler> GetStraggler(string managedDeviceId);
        Task<List<Straggler>> GetStragglerList(int minCount);
        Task UpdateStraggler(Straggler straggler);
        Task DeleteStraggler(Straggler straggler);
        Task UpdateStragglerAsErrored(Straggler straggler);

        Task<List<Straggler>> GetStragglersProcessedByUD(int minCount);



    }
}
