using CorporateIdentifierSync.Interfaces;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta.Models;
using DelegationStationShared.Extensions;
using Device = DelegationStationShared.Models.Device;
using DelegationStationShared;
using DelegationStationShared.Enums;

namespace CorporateIdentifierSync
{
    public class ConfirmSync
    {
        private readonly ILogger _logger;
        private readonly ICosmosDbService _dbService;
        private readonly IGraphBetaService _graphBetaService;

        public ConfirmSync(ILoggerFactory loggerFactory, ICosmosDbService dbService, IGraphBetaService graphBetaService)
        {
            _logger = loggerFactory.CreateLogger<ConfirmSync>();
            _dbService = dbService;
            _graphBetaService = graphBetaService;
        }

        [Function("ConfirmSync")]
        public async Task Run([TimerTrigger("%ConfirmSyncTriggerTime%")] TimerInfo myTimer)
        {

            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation($"C# Timer trigger function executed at: {DateTime.Now}", fullMethodName);

            if (myTimer.ScheduleStatus is not null)
            {
                _logger.DSLogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}", fullMethodName);
            }


            bool isCorpIDSyncEnabled = false;
            bool result = bool.TryParse(Environment.GetEnvironmentVariable("EnableCorpIDSync", EnvironmentVariableTarget.Process), out isCorpIDSyncEnabled);
            if (!result)
            {
                _logger.DSLogError("CorpIDSyncEnabled not set or not a valid boolean. Disabling sync.", fullMethodName);
            }
            else if (!isCorpIDSyncEnabled)
            {
                _logger.DSLogInformation("CorpIDSyncEnabled set to false. Disabling sync.", fullMethodName);
            }

            if (!isCorpIDSyncEnabled)
            {
                _logger.DSLogInformation("Syncing not enabled.  No work to do.  Function is exiting.", fullMethodName);
                return;
            }

            int deviceCount = 0;
            int intervalHours = 0;
            bool result2 = int.TryParse(Environment.GetEnvironmentVariable("SyncIntervalHours", EnvironmentVariableTarget.Process), out intervalHours);
            if (!result2)
            {
                _logger.DSLogError("SyncIntervalHours is not set or not a valid integer. Exiting.", fullMethodName);
                return;
            }
            _logger.DSLogInformation($"Checking devices last checked over {intervalHours} hours ago.", fullMethodName);

            // Get list of tags set to sync
            List<string> tagsWithSyncEnabled = await _dbService.GetSyncEnabledDeviceTags();

            // Get all devices with sync date older than X
            List<Device> devicesToCheck = await _dbService.GetDevicesSyncedBefore(DateTime.UtcNow.AddHours(-intervalHours));
            _logger.DSLogInformation($"Found {devicesToCheck.Count} devices to check.", fullMethodName);

            foreach (Device device in devicesToCheck)
            {
                _logger.DSLogInformation($"-----Confirming corporate identifier is synced for {device.Make} {device.Model} {device.SerialNumber} -----", fullMethodName);

                bool tagSetToSync = tagsWithSyncEnabled.Contains(device.Tags[0]);
                var corpIDFound = false;
                var corpIDUpdated = false;
                var successfullyUnsynced = false;

                if (tagSetToSync)
                {
                    // Query to see if device is still in CorporateIdentifiers
                    if (!String.IsNullOrEmpty(device.CorporateIdentityID))
                    {
                        corpIDFound = await _graphBetaService.CorporateIdentifierExists(device.CorporateIdentityID);
                    }

                    // If not found, add it back
                    if (!corpIDFound)
                    {
                        _logger.DSLogInformation("Corporate Identifier not found, adding back to CorporateIdentifiers", fullMethodName);

                        string identifier = "";
                        if ((device.OS == DeviceOS.Windows) || (device.OS == DeviceOS.Unknown))
                        {
                            identifier = $"{device.Make},{device.Model},{device.SerialNumber}";

                        }
                        else
                        {
                            identifier = device.SerialNumber;
                        }


                        try
                        {
                            ImportedDeviceIdentityType corpIDType = CorpIDUtilities.GetCorpIDTypeForOS(device.OS);
                            ImportedDeviceIdentity deviceIdentity = await _graphBetaService.AddCorporateIdentifier(corpIDType, identifier);

                            device.CorporateIdentityID = deviceIdentity.Id;
                            device.CorporateIdentity = deviceIdentity.ImportedDeviceIdentifier;
                            corpIDUpdated = true;
                        }
                        catch (Exception ex)
                        {
                            _logger.DSLogError($"Error adding corporate identifier for device {device.Id}: {ex.Message}", fullMethodName);
                        }

                    }
                    else
                    {
                        _logger.DSLogInformation("Corporate Identifier found, updating sync date", fullMethodName);
                    }

                    if (corpIDFound || corpIDUpdated)
                    {
                        device.Status = DeviceStatus.Synced;
                    }
                }
                else  // !tagSetToSync
                {
                    _logger.DSLogInformation($"Tag {device.Tags[0]} is not set to sync.", fullMethodName);

                    // if device was synced remove from CorporateIdentifiers
                    if (device.Status == DeviceStatus.Synced)
                    {
                        _logger.DSLogInformation("Device was synced, but is tag is not set to sync.  Removing from CorporateIdentifiers", fullMethodName);
                        try
                        {
                            successfullyUnsynced = await _graphBetaService.DeleteCorporateIdentifier(device.CorporateIdentityID);
                            device.Status = DeviceStatus.NotSyncing;
                            device.CorporateIdentityID = "";
                            device.CorporateIdentity = "";
                        }
                        catch (Exception ex)
                        {
                            _logger.DSLogError($"Error deleting corporate identifier for device {device.Id}: {ex.Message}", fullMethodName);
                        }
                    }
                    else
                    {
                        successfullyUnsynced = true;
                    }
                }

                if ((!tagSetToSync && successfullyUnsynced) || corpIDFound || corpIDUpdated)
                {

                    // Update the sync date and status for devices successfully processed
                    // For devices that error, we want to try again on the next run
                    device.LastCorpIdentitySync = DateTime.UtcNow;
                    _logger.DSLogInformation($"New sync date: {device.LastCorpIdentitySync}", fullMethodName);

                    // Update device entry in DS
                    try
                    {
                        await _dbService.UpdateDevice(device);
                        deviceCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.DSLogError($"Error updating device {device.Id}: {ex.Message}", fullMethodName);
                    }
                }

            }

            _logger.LogInformation($"Successfully updated {deviceCount} devices.", fullMethodName);
        }
    }
}
