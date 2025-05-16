using CorporateIdentifierSync.Interfaces;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;
using DelegationStationShared.Extensions;
using Device = DelegationStationShared.Models.Device;
using DelegationStationShared;

namespace CorporateIdentifierSync
{
    public class DeviceDeletion
    {
        private readonly ILogger<DeviceDeletion> _logger;
        private readonly ICosmosDbService _dbService;
        private readonly IGraphService _graphService;
        private readonly IGraphBetaService _graphBetaService;

        public DeviceDeletion(ILogger<DeviceDeletion> logger, ICosmosDbService dbService, IGraphService graphService, IGraphBetaService graphBetaService)
        {
            _logger = logger;
            _dbService = dbService;
            _graphService = graphService;
            _graphBetaService = graphBetaService;
        }

        [Function("DeviceDeletion")]
        public async Task Run([TimerTrigger("%DeleteDevicesTriggerTime%")] TimerInfo myTimer)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("C# Timer trigger function executed at: " + DateTime.Now, fullMethodName);
            if (myTimer.ScheduleStatus is not null)
            {
                _logger.DSLogInformation("Next timer schedule at: " + myTimer.ScheduleStatus.Next, fullMethodName);
            }


            bool isCorpIDSyncEnabled = false;
            bool result = bool.TryParse(Environment.GetEnvironmentVariable("EnableCorpIDSync", EnvironmentVariableTarget.Process), out isCorpIDSyncEnabled);
            if (!result)
            {
                _logger.DSLogError("CorpIDSyncEnabled not set or not a valid boolean. Disabling CorpID deletions.", fullMethodName);
            }
            else if (!isCorpIDSyncEnabled)
            {
                _logger.DSLogInformation("CorpIDSyncEnabled set to false. Disabling CorpID deletions.", fullMethodName);
            }

            //
            // Get All devices marked for deletion
            //
            List<Device> devicesToDelete = await _dbService.GetDevicesMarkedForDeletion();
            _logger.DSLogInformation($"Found {devicesToDelete.Count} devices to delete.", fullMethodName);


            // For each set blank Corporate Identifier values
            int deviceCount = 0;
            foreach (Device device in devicesToDelete)
            {
                _logger.DSLogInformation($"-----Deleting device {device.Id}.-----", fullMethodName);


                // Delete from Managed Devices
                bool delManagedDevice = false;
                ManagedDevice managedDevice = null;
                try
                {
                    managedDevice = await _graphService.GetManagedDevice(device.Make, device.Model, device.SerialNumber);

                    if (managedDevice != null && managedDevice.Id != null)
                    {
                        _logger.DSLogInformation($"Found managed device {managedDevice.Id} in Intune that matches device {device.Make} {device.Model} {device.SerialNumber}.", fullMethodName);
                        delManagedDevice = await _graphService.DeleteManagedDevice(managedDevice.Id);
                        _logger.DSLogInformation($"Successfully deleted managed device {managedDevice.Id} for {device.Make} {device.Model} {device.SerialNumber}", fullMethodName);
                    }
                    else
                    {
                        // Setting as deleted since it's not present
                        _logger.DSLogInformation($"No managed device to delete for {device.Make} {device.Model} {device.SerialNumber}", fullMethodName);
                        delManagedDevice = true;
                    }
                }
                catch (Exception ex)
                {
                    if (managedDevice == null)
                    {
                        _logger.DSLogException($"Unable to query managed device for {device.Make} {device.Model} {device.SerialNumber}", ex, fullMethodName);
                    }
                    else
                    {
                        _logger.DSLogException($"Unable to delete managed device: {managedDevice.Id} {device.Make} {device.Model} {device.SerialNumber}", ex, fullMethodName);
                    }
                    delManagedDevice = false;
                }

                //
                //  Only continues with deletions if successfully removed managed device entry
                //
                if (delManagedDevice)
                {
                    bool delCorpID = false;

                    if (isCorpIDSyncEnabled)
                    {
                        // Delete from Corporate Identifiers
                        if (!String.IsNullOrEmpty(device.CorporateIdentityID))
                        {
                            delCorpID = await _graphBetaService.DeleteCorporateIdentifier(device.CorporateIdentityID);
                            if (delCorpID)
                            {
                                _logger.DSLogInformation($"Successfully deleted Corporate Identifier: {device.CorporateIdentity}", fullMethodName);
                            }
                        }
                        else
                        {
                            _logger.LogInformation($"Device not synced yet. No Corp Identifier to delete.  {device.Make} {device.Model} {device.SerialNumber}", fullMethodName);
                            delCorpID = true;
                        }
                    }
                    else
                    {
                        delCorpID = true;
                    }

                    // Delete from Delegation Station
                    if (delCorpID)
                    {
                        try
                        {
                            await _dbService.DeleteDevice(device);
                            deviceCount++;
                            _logger.DSLogInformation($"Successfully deleted device from Delegation Station: {device.Make} {device.Model} {device.SerialNumber}.", fullMethodName);
                        }
                        catch (Exception ex)
                        {
                            _logger.DSLogException($"Deletion from Delegation Station failed for device {device.Id}.", ex, fullMethodName);
                        }
                    }
                    else
                    {
                        _logger.DSLogError($"Deletion from Corporate Identifiers failed for device {device.Id}.  Not deleting from Delegation Station", fullMethodName);
                    }
                }
                else
                {
                    if (managedDevice != null && managedDevice.Id != null)
                    {
                        _logger.DSLogError($"Deletion from Intune failed for device {device.Id} (managedDevice ID: {managedDevice.Id}).  Not deleting from Delegation Station", fullMethodName);
                    }
                    else
                    {
                        _logger.DSLogError($"Deletion from Intune failed for device {device.Id} Not deleting from Delegation Station", fullMethodName);
                    }

                }
            }

            _logger.DSLogInformation($"Successfully deleted {deviceCount} devices.", fullMethodName);
        }
    }
}
