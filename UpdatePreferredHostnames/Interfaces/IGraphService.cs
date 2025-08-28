using DelegationStationShared.Models;
using Microsoft.Graph.Models;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;

namespace UpdatePreferredHostnames.Interfaces
{
    public interface IGraphService
    {
        Task<ManagedDevice> GetManagedDevice(string deviceID);
        Task AddDeviceToAzureADGroup(string deviceId, string deviceObjectId, DeviceUpdateAction group);
        Task AddDeviceToAzureAdministrativeUnit(string deviceId, string deviceObjectId, DeviceUpdateAction adminUnit);
        Task UpdateAttributesOnDeviceAsync(string deviceId, string deviceObjectId, List<DeviceUpdateAction> deviceUpdateActions);
        Task<List<ManagedDevice>> GetNewDeviceManagementObjectsAsync(DateTime dateTime);
        Task<string> GetDeviceObjectID(string azureADDeviceID);

    }
}