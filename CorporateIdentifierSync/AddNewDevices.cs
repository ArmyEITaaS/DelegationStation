using CorporateIdentifierSync.Enums;
using CorporateIdentifierSync.Interfaces;
using DelegationStationShared;
using DelegationStationShared.Enums;
using DelegationStationShared.Extensions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta.Models;
using Device = DelegationStationShared.Models.Device;

namespace CorporateIdentifierSync
{
    public class AddNewDevices
    {
        private readonly ILogger<AddNewDevices> _logger;
        private readonly ICosmosDbService _dbService;
        private readonly IGraphBetaService _graphBetaService;
        private readonly IFunctionSingletonLock _singletonLock;

        private bool _IsCorpIDSyncEnabled;
        private int _BatchSize;
        private int _MaxCorpIDsAllowed;
        private int _MaxCorpIDRetries;

        public AddNewDevices(ILoggerFactory loggerFactory, ICosmosDbService dbService, IGraphBetaService graphBetaService, IFunctionSingletonLock singletonLock)
        {
            _logger = loggerFactory.CreateLogger<AddNewDevices>();
            _dbService = dbService;
            _graphBetaService = graphBetaService;
            _singletonLock = singletonLock;
        }

        public void GetEnvironmentVariables()
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            //
            // Get CorpID sync flag
            //
            _IsCorpIDSyncEnabled = false;
            bool result = bool.TryParse(Environment.GetEnvironmentVariable("EnableCorpIDSync"), out _IsCorpIDSyncEnabled);
            if (!result)
            {
                _logger.DSLogError("EnableCorpIDSync not set or not a valid boolean.  Defaulting to disabled.", fullMethodName);
            }

            //
            // Get batch size for adding new devices
            //
            _BatchSize = 5000;
            string batchSizeString = Environment.GetEnvironmentVariable("AddDeviceBatchSize");
            if (!int.TryParse(batchSizeString, out int bs) || bs <= 0)
            {
                _logger.DSLogError($"BatchSize is not set or invalid. Using default value: {_BatchSize}.", fullMethodName);
            }
            else
            {
                _BatchSize = bs;
                _logger.DSLogInformation($"Using BatchSize: {_BatchSize}.", fullMethodName);
            }

            //
            // Getting Max Allowed Corporate ID entries
            //
            _MaxCorpIDsAllowed = 10000;
            string maxCorpIDsString = Environment.GetEnvironmentVariable("MAX_CORPIDS_ALLOWED");
            if (!int.TryParse(maxCorpIDsString, out int max) || max <= 0)
            {
                _logger.DSLogError($"Max Corp IDS Allowed is not set or invalid.  Using default value: {_MaxCorpIDsAllowed}.", fullMethodName);
            }
            else
            {
                _MaxCorpIDsAllowed = max;
                _logger.DSLogInformation($"Maximum allowed Corporate Identifers for the tenant is set to: {_MaxCorpIDsAllowed}", fullMethodName);
            }

            //
            // Set max times we'll retry adding a device to Corporate Identifiers before marking it as Failed
            //
            _MaxCorpIDRetries = 10;
            string maxRetriesString = Environment.GetEnvironmentVariable("MAX_CORPID_RETRIES");
            if (!int.TryParse(maxRetriesString, out int retries) || retries <= 0)
            {
                _logger.DSLogError($"MAX_CORPID_RETRIES is not set or invalid. Using default value: {_MaxCorpIDRetries}.", fullMethodName);
            }
            else
            {
                _MaxCorpIDRetries = retries;
                _logger.DSLogInformation($"Max Corporate Identifier retries before marking device as Failed: {_MaxCorpIDRetries}.", fullMethodName);
            }
        }


        [Function("AddNewDevices")]
        public async Task Run([TimerTrigger("%AddDevicesTriggerTime%")] TimerInfo myTimer)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            await using var handle = await _singletonLock.TryAcquireAsync(nameof(AddNewDevices));
            if (handle is null)
            {
                _logger.DSLogWarning("Another instance of AddNewDevices is already running. Exiting.", fullMethodName);
                return;
            }

            _logger.DSLogInformation("C# Timer trigger function executed at: " + DateTime.Now, fullMethodName);
            if (myTimer.ScheduleStatus is not null)
            {
                _logger.DSLogInformation("Next timer schedule at: " + myTimer.ScheduleStatus.Next, fullMethodName);
            }

            GetEnvironmentVariables();

            //
            //  We don't need to keep going if either sync is disabled
            //
            if (!_IsCorpIDSyncEnabled)
            {
                _logger.DSLogInformation("Syncing not enabled.  No work to do.  Function is exiting.", fullMethodName);
                return;
            }


            //
            // First process all devices added that aren't to be synced
            //
            List<string> nonSyncingTagIDs = await _dbService.GetNonSyncingDeviceTags();
            List<Device> notSyncingDevices = await _dbService.GetAddedDevicesNotSyncing(nonSyncingTagIDs, _BatchSize);
            _logger.DSLogInformation("Processing devices in tags that aren't set to sync to CorpIDs first", fullMethodName);
            foreach (Device device in notSyncingDevices)
            {
                try
                {
                    _logger.DSLogInformation($"Device {device.Make} {device.Model} {device.SerialNumber} tag {device.Tags[0]} is not enabled for sync.", fullMethodName);
                    device.Status = DeviceStatus.NotSyncing;
                    device.LastCorpIdentitySync = DateTime.UtcNow;

                    // Update the DB entry with the new Corporate Identifier info
                    await _dbService.UpdateDevice(device);

                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.DSLogException($"Device not found to updated.  Likely deleted after marked to process: {device.Make} {device.Model} {device.SerialNumber}", ex, fullMethodName);
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
                {
                    // Singleton lock prevents another AddNewDevices instance from racing us.
                    // Most likely cause: user moved the device to Deleting via the UI.
                    // Since we're not syncing this device to CorpIDs anyway, no action is required.
                    _logger.DSLogWarning(
                        $"Device {device.Make} {device.Model} {device.SerialNumber} was modified concurrently " +
                        $"(likely user-initiated). No action required for non-syncing device.",
                        fullMethodName);
                }
                catch (Exception dbEx)
                {
                    _logger.DSLogException($"Device entry not updated for non-syncing device (should be retried next run):  {device.Make} {device.Model} {device.SerialNumber}", dbEx, fullMethodName);
                }
            }

            //
            // Now process devices to sync (which will be limited by batch settings)
            // CorpID Capacity Manager will be used to ensure we stay under configured limit
            //
            var capacityManager = new CorpIdCapacityManager(_dbService, _logger, _MaxCorpIDsAllowed);

            int availableCorpIDs;
            try
            {
                availableCorpIDs = await capacityManager.GetAvailableCorpIDCount(CancellationToken.None);
                _logger.DSLogInformation($"Available Corporate ID slots: {availableCorpIDs}.", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Exiting.  Unable to retrieve available Corporate ID slots.", ex, fullMethodName);
                return;
            }

            // If we're out of CorpIds, it's time to exit
            // If we are out of corpIDs, any new devices needing syncing will stay in Added state until some are made available
            if (availableCorpIDs <= 0)
            {
                _logger.DSLogWarning("No available Corporate ID slots. Function is exiting.", fullMethodName);
                return;
            }

            int requestSize = Math.Min(_BatchSize, availableCorpIDs);
            _logger.DSLogInformation($"Reserving {requestSize} CorporateID entries", fullMethodName);

            //Reserve Corporate ID slots for this batch
            int reservedSlots;
            try
            {
                reservedSlots = await capacityManager.ReserveCorpIDs(requestSize, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Exiting.  Unable to reserve slots for CorpIDs: ", ex, fullMethodName);
                return;
            }

            if (reservedSlots <= 0)
            {
                _logger.DSLogWarning("No available Corporate ID slots at reservation time.  Function is exiting.", fullMethodName);
                return;
            }
            _logger.DSLogInformation($"Successfully reserved {reservedSlots} Corporate ID slots for this batch.", fullMethodName);

            // Get All Devices without Corporate Identifier values or fields
            List<Device> devicesToMigrate;
            List<string> syncingTagIDs = await _dbService.GetSyncingDeviceTags();
            try
            {
                devicesToMigrate = await _dbService.GetAddedDevicesToSync(syncingTagIDs, reservedSlots);
                _logger.DSLogInformation($"Found {devicesToMigrate.Count} devices to migrate.", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogError($"Exiting. Error getting devices to migrate: {ex.Message}", fullMethodName);
                int available;
                try
                {
                    available = await capacityManager.CommitCorpIDCount(reservedSlots, 0, CancellationToken.None);
                    _logger.DSLogInformation($"Returned {reservedSlots} reserved CorpIDs. {available} CorpIDs now available.  ");
                }
                catch (Exception ex2)
                {
                    _logger.DSLogException($"Unable to release {reservedSlots} unused reserved CorpIDs.", ex2, fullMethodName);
                }
                return;
            }


            // For each device set blank Corporate Identifier values
            int deviceCount = 0;
            int devicesSynced = 0;
            int totalDevices = devicesToMigrate.Count;
            foreach (Device device in devicesToMigrate)
            {

                // Set OS as Unknown if not set
                // For backwards compatibility Unknown is handled like Windows
                if (device.OS == null)
                {
                    device.OS = DeviceOS.Unknown;
                }

                // Add the Corporate Identifier
                try
                {
                    _logger.DSLogInformation($"-----Adding Corporate Identifier for device {device.Make} {device.Model} {device.SerialNumber}.-----", fullMethodName);

                    string identifier = "";
                    if ((device.OS == DeviceOS.Windows) || (device.OS == DeviceOS.Unknown))
                    {

                        // Putting make and model in quotes to handle commas
                        string escapedMake = "\"" + device.Make + "\"";
                        string escapedModel = "\"" + device.Model + "\"";
                        identifier = $"{escapedMake},{escapedModel},{device.SerialNumber}";
                    }
                    else
                    {
                        identifier = device.SerialNumber;
                    }

                    ImportedDeviceIdentityType corpIDType = CorpIDUtilities.GetCorpIDTypeForOS(device.OS);
                    ImportedDeviceIdentity deviceIdentity = await _graphBetaService.AddCorporateIdentifier(corpIDType, identifier);
                    devicesSynced++;

                    // Set the Corporate Identifier values
                    device.CorporateIdentityID = deviceIdentity.Id;
                    device.CorporateIdentity = deviceIdentity.ImportedDeviceIdentifier;
                    device.Status = DeviceStatus.Synced;
                    device.LastCorpIdentitySync = DateTime.UtcNow;
                    device.CorpIDFailureCount = 0;


                    deviceCount++;

                    _logger.DSLogInformation($"Successfully added Corporate Identifier for device {deviceCount}/{totalDevices}:  {device.Make} {device.Model} {device.SerialNumber}.", fullMethodName);
                }
                catch (Exception ex)
                {
                    _logger.DSLogException($"Error adding Corporate Identifier for device {device.Make} {device.Model} {device.SerialNumber}: ", ex, fullMethodName);
                    device.CorpIDFailureCount++;
                    if (device.CorpIDFailureCount > _MaxCorpIDRetries)
                    {
                        _logger.DSLogError($"Device {device.Make} {device.Model} {device.SerialNumber} has exceeded max Corporate Identifier retries. Marking as Failed.", fullMethodName);
                        device.Status = DeviceStatus.Failed;
                    }
                    else
                    {
                        _logger.DSLogWarning($"Device {device.Make} {device.Model} {device.SerialNumber} has failed to sync Corporate Identifier {device.CorpIDFailureCount} times. It will be retried in the next sync cycle.", fullMethodName);
                    }
                }  // closes the catch

                try
                {
                    // Update the DB entry with the new Corporate Identifier info
                    await _dbService.UpdateDevice(device);
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.DSLogWarning($"Device {device.Make} {device.Model} {device.SerialNumber} was deleted during processing. Cleaning up Corp ID from Graph.", fullMethodName);

                    // Roll back the Graph side-effect
                    if (!string.IsNullOrEmpty(device.CorporateIdentityID))
                    {
                        var rollbackResult = await _graphBetaService.DeleteCorporateIdentifier(device.CorporateIdentityID);
                        if (rollbackResult == DeleteCorpIdResult.Error)
                        {
                            _logger.DSLogError($"Failed to roll back Corp ID {device.CorporateIdentityID} from Graph. Manual cleanup may be required.", fullMethodName);
                        }
                        else
                        {
                            _logger.DSLogInformation($"Successfully rolled back Corp ID {device.CorporateIdentityID} from Graph.", fullMethodName);
                            devicesSynced--;
                        }
                    }
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
                {
                    // Singleton lock guarantees no other AddNewDevices instance ran concurrently.
                    // The only realistic concurrent modifier is a user marking the device for deletion
                    // via the UI between our read and our write.
                    _logger.DSLogWarning($"Device {device.Make} {device.Model} {device.SerialNumber} was modified concurrently. Checking current state.", fullMethodName);

                    Device? currentDevice = null;
                    try
                    {
                        currentDevice = await _dbService.GetDevice(device.Id, device.PartitionKey);
                    }
                    catch (Exception fetchEx)
                    {
                        _logger.DSLogException(
                            $"Unable to fetch current state of device {device.Make} {device.Model} {device.SerialNumber} after PreconditionFailed. " +
                            $"Corp ID {device.CorporateIdentityID} left in Graph; will reconcile on next run.",
                            fetchEx, fullMethodName);
                        // Don't decrement devicesSynced — Corp ID is still in Graph and counted.
                        continue;
                    }

                    if (currentDevice is null || currentDevice.Status == DeviceStatus.Deleting)
                    {
                        // User marked the device for deletion mid-run. Roll back the Corp ID we just added.
                        _logger.DSLogWarning(
                            $"Device {device.Make} {device.Model} {device.SerialNumber} is gone or marked for deletion. Rolling back Corp ID {device.CorporateIdentityID}.",
                            fullMethodName);

                        if (!string.IsNullOrEmpty(device.CorporateIdentityID))
                        {
                            var rollbackResult = await _graphBetaService.DeleteCorporateIdentifier(device.CorporateIdentityID);
                            if (rollbackResult == DeleteCorpIdResult.Error)
                            {
                                _logger.DSLogError(
                                    $"Failed to roll back Corp ID {device.CorporateIdentityID}. Manual cleanup may be required.",
                                    fullMethodName);
                            }
                            else
                            {
                                _logger.DSLogInformation($"Rolled back Corp ID {device.CorporateIdentityID}.", fullMethodName);
                                devicesSynced--;
                            }
                        }
                    }
                    else
                    {
                        // Singleton makes Added/Failed/Synced transitions by another writer impossible here.
                        // If we hit this, something unexpected happened — log and leave Corp ID in Graph;
                        // ConfirmSync/ReconcileSyncState will reconcile on the next pass.
                        _logger.DSLogWarning(
                            $"Device {device.Make} {device.Model} {device.SerialNumber} unexpectedly in state '{currentDevice.Status}' after PreconditionFailed. " +
                            $"Leaving Corp ID {device.CorporateIdentityID} in Graph for downstream reconciliation.",
                            fullMethodName);
                    }
                }
                catch (Exception dbEx)
                {
                    _logger.DSLogException($"Device entry not updated - CorpIDStatus may not be in sync: {device.Make} {device.Model} {device.SerialNumber}", dbEx, fullMethodName);
                }
            }  // closes foreach

            // Release any unused reserved Corporate ID slots
            try
            {
                int nowAvailable = await capacityManager.CommitCorpIDCount(reservedSlots, devicesSynced, CancellationToken.None);
                _logger.DSLogInformation($"Successfully migrated {deviceCount} devices. {devicesSynced} devices were synced with Corporate Identifiers.", fullMethodName);
                if (nowAvailable > 0)
                {
                    _logger.DSLogInformation($"Current available Corporate ID slots: {nowAvailable}.", fullMethodName);
                }
                else
                {
                    _logger.DSLogWarning($"Current available Corporate ID slots: {nowAvailable}.", fullMethodName);
                }
            }
            catch (Exception ex)
            {
                _logger.DSLogException($"Failed to update Capacitymanager to update that of {reservedSlots}, {devicesSynced} were used.", ex, fullMethodName);
            }

        }

    }
}
