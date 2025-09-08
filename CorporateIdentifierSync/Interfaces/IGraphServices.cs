using Microsoft.Graph.Models;

namespace CorporateIdentifierSync.Interfaces
{
    public interface IGraphService
    {
        Task<ManagedDevice> GetManagedDevice(string make, string model, string serialNum);

        //Task<bool> DeleteManagedDevice(string managedDeviceID);
    }
}
