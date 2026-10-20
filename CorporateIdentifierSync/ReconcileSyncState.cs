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
    public class ReconcileSyncState
    {
        private readonly ILogger _logger;
        private readonly ICosmosDbService _dbService;
        private readonly IGraphBetaService _graphBetaService;
        private readonly IFunctionSingletonLock _singletonLock;

        private bool _IsCorpIDSyncEnabled;
        private int _BatchSize;
        private int _MaxCorpIDsAllowed;

        public ReconcileSyncState(
            ILoggerFactory loggerFactory,
            ICosmosDbService dbService,
            IGraphBetaService graphBetaService,
            IFunctionSingletonLock singletonLock)
        {
            _logger = loggerFactory.CreateLogger<ReconcileSyncState>();
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
                _logger.DSLogError("EnableCorpIDSync not set or not a valid boolean. Defaulting to disabled.", fullMethodName);
            }

            //
            // Get batch size for reconciling devices
            //
            _BatchSize = 1000;
            string batchSizeString = Environment.GetEnvironmentVariable("ReconcileSyncBatchSize");
            if (!int.TryParse(batchSizeString, out int bs) || bs <= 0)
            {
                _logger.DSLogWarning($"ReconcileSyncBatchSize is not set or invalid. Using default value: {_BatchSize}.", fullMethodName);
            }
            else
            {
                _BatchSize = bs;
                _logger.DSLogInformation($"Using ReconcileSyncBatchSize: {_BatchSize}.", fullMethodName);
            }

            //
            // Get maximum allowed Corporate ID entries
            //
            _MaxCorpIDsAllowed = 10000;
            string maxCorpIDsString = Environment.GetEnvironmentVariable("MAX_CORPIDS_ALLOWED");
            if (!int.TryParse(maxCorpIDsString, out int max) || max <= 0)
            {
                _logger.DSLogError($"MAX_CORPIDS_ALLOWED is not set or invalid. Using default value: {_MaxCorpIDsAllowed}.", fullMethodName);
            }
            else
            {
                _MaxCorpIDsAllowed = max;
                _logger.DSLogInformation($"Maximum allowed Corporate Identifiers for the tenant is set to: {_MaxCorpIDsAllowed}.", fullMethodName);
            }
        }

        [Function("ReconcileSyncState")]
        public async Task Run([TimerTrigger("%ReconcileSyncStateTriggerTime%")] TimerInfo myTimer)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            await using var handle = await _singletonLock.TryAcquireAsync(nameof(ReconcileSyncState));
            if (handle is null)
            {
                _logger.DSLogWarning("Another instance of ReconcileSyncState is already running. Exiting.", fullMethodName);
                return;
            }

            _logger.DSLogInformation($"C# Timer trigger function executed at: {DateTime.Now}", fullMethodName);

            if (myTimer.ScheduleStatus is not null)
            {
                _logger.DSLogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}", fullMethodName);
            }

            GetEnvironmentVariables();

            //
            // Check if syncing is enabled via environment variable before doing any work
            //

            if (!_IsCorpIDSyncEnabled)
            {
                _logger.DSLogInformation("Syncing not enabled. No work to do. Function is exiting.", fullMethodName);
                return;
            }

            //
            // Remove any devices from CorpID that are synced, but are no longer in a tag set to sync
            //
            int corpIDsRemoved = await RemoveSyncedDevicesInDisabledTagsAsync(_BatchSize);
            _logger.DSLogInformation($"Removed Corp IDs for {corpIDsRemoved} devices in disabled tags.", fullMethodName);

            //
            // Add any devices to CorpID that are not syncing, but are in a tag set to sync - up to the batch size and available capacity
            //
            int corpIDsAdded = await AddNotSyncingDevicesInEnabledTagsAsync(_BatchSize);
            _logger.DSLogInformation($"Added Corp IDs for {corpIDsAdded} devices in enabled tags.", fullMethodName);

            _logger.DSLogInformation("ReconcileSyncState completed.", fullMethodName);
        }

        private async Task<int> RemoveSyncedDevicesInDisabledTagsAsync(int batchSize)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("--- Section 1: Removing Corp IDs for Synced devices in disabled tags. ---", fullMethodName);

            List<string> tagsWithSyncDisabled = await _dbService.GetNonSyncingDeviceTags();
            _logger.DSLogInformation($"Found {tagsWithSyncDisabled.Count} tags with sync disabled.", fullMethodName);

            int updatedCount = 0;
            int corpIDsRemoved = 0;
            int failureCount = 0;

            List<Device> devicesToUnsync = await _dbService.GetSyncedDevicesInTags(tagsWithSyncDisabled, batchSize);

            _logger.DSLogInformation($"Found {devicesToUnsync.Count} Synced devices in disabled tags.", fullMethodName);

            foreach (Device device in devicesToUnsync)
            {
                _logger.DSLogInformation($"-----Removing Corp ID for {device.Make} {device.Model} {device.SerialNumber}.-----", fullMethodName);

                bool hadCorpID = !string.IsNullOrEmpty(device.CorporateIdentityID);
                bool corpIDAbsent = false;
                bool deletedCorpID = false;

                if (hadCorpID)
                {
                    DeleteCorpIdResult graphDeleteResult = await _graphBetaService.DeleteCorporateIdentifier(device.CorporateIdentityID);
                    corpIDAbsent = graphDeleteResult == DeleteCorpIdResult.Success || graphDeleteResult == DeleteCorpIdResult.NotFound;
                    deletedCorpID = graphDeleteResult == DeleteCorpIdResult.Success;
                    if (corpIDAbsent)
                    {
                        if (deletedCorpID)
                        {
                            _logger.DSLogInformation($"Removed Corp ID from Graph for device {device.Make} {device.Model} {device.SerialNumber}.", fullMethodName);
                        }
                        else
                        {
                            _logger.DSLogWarning($"Corp ID {device.CorporateIdentityID} for device {device.Make} {device.Model} {device.SerialNumber} not found when deletion attempted.", fullMethodName);
                        }
                    }
                    else
                    {
                        _logger.DSLogError($"Error removing Corp ID from Graph for device {device.Make} {device.Model} {device.SerialNumber}.", fullMethodName);
                    }
                }
                else
                {
                    corpIDAbsent = true;
                }

                if (corpIDAbsent)
                {
                    device.Status = DeviceStatus.NotSyncing;
                    device.CorporateIdentityID = string.Empty;
                    device.CorporateIdentity = string.Empty;
                    device.LastCorpIdentitySync = DateTime.UtcNow;

                    try
                    {
                        await _dbService.UpdateDevice(device);
                        updatedCount++;
                        if (deletedCorpID)
                        {
                            corpIDsRemoved++;
                        }
                    }
                    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        // Device row is gone (DeviceDeletion or another writer removed it).
                        // We already removed the Corp ID from Graph in this run, so WE own the
                        // capacity release — DeviceDeletion will see NotFound from Graph and skip it.
                        _logger.DSLogInformation($"Device {device.Make} {device.Model} {device.SerialNumber} row not found on update; Corp ID already removed from Graph by this run. Releasing capacity.", fullMethodName);
                        if (deletedCorpID)
                        {
                            corpIDsRemoved++;
                        }
                    }
                    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
                    {
                        // Corp ID was already deleted from Graph but another writer modified this device concurrently.
                        // Legitimate concurrent writers: ConfirmSync (re-adding a missing Corp ID) or user UI
                        // (flipping the tag, marking for deletion, or editing fields).
                        _logger.DSLogWarning($"Device {device.Make} {device.Model} {device.SerialNumber} was modified concurrently after Corp ID removal. Reconciling.", fullMethodName);

                        Device? freshDevice = null;
                        bool freshReadFailed = false;
                        try
                        {
                            freshDevice = await _dbService.GetDevice(device.Id, device.PartitionKey);
                        }
                        catch (Exception retryEx)
                        {
                            freshReadFailed = true;
                            _logger.DSLogException(
                                $"Failed to re-read device {device.Make} {device.Model} {device.SerialNumber} after 412. Corp ID was removed from Graph; " +
                                $"ConfirmSync will re-add on next run if device is still Synced.",
                                retryEx, fullMethodName);
                        }

                        if (freshReadFailed)
                        {
                            // Nothing more we can do this run — defer to next pass.
                        }
                        else if (freshDevice is null ||
                                 freshDevice.Status == DeviceStatus.Deleting ||
                                 freshDevice.Status == DeviceStatus.NotSyncing)
                        {
                            // Already in (or heading toward) the intended end state — no row update needed.
                            // But WE removed the Corp ID from Graph this run, so WE must release its capacity slot;
                            // no other writer is tracking that.
                            _logger.DSLogInformation(
                                $"Device {device.Make} {device.Model} {device.SerialNumber} is in state {freshDevice?.Status.ToString() ?? "deleted"}; " +
                                $"no row update needed. Releasing Corp ID capacity since Graph delete was performed here.",
                                fullMethodName);
                            if (deletedCorpID)
                            {
                                corpIDsRemoved++;
                            }
                        }
                        else
                        {
                            // Status is Synced, Added, or Failed. Before overwriting to NotSyncing, re-check
                            // that the tag is still disabled — the user may have re-enabled sync between our
                            // initial read and the conflict, in which case unsyncing would undo their intent.
                            List<string> currentDisabledTags = null;
                            bool tagCheckFailed = false;
                            try
                            {
                                currentDisabledTags = await _dbService.GetNonSyncingDeviceTags();
                            }
                            catch (Exception tagEx)
                            {
                                tagCheckFailed = true;
                                _logger.DSLogException(
                                    $"Failed to re-check tag sync state for device {device.Make} {device.Model} {device.SerialNumber} after 412. " +
                                    $"Skipping unsync to avoid overriding potential user intent. " +
                                    $"ConfirmSync will reconcile Corp ID on next run.",
                                    tagEx, fullMethodName);
                            }

                            bool tagStillDisabled = !tagCheckFailed &&
                                                    freshDevice.Tags.Count > 0 &&
                                                    currentDisabledTags.Contains(freshDevice.Tags[0]);

                            if (tagCheckFailed)
                            {
                                // Already logged above — defer to next run.
                            }
                            else if (!tagStillDisabled)
                            {
                                // User re-enabled sync on this tag. Honor that intent; leave Corp ID reconciliation
                                // to ConfirmSync (which will re-add the same Id since the hash is deterministic).
                                _logger.DSLogWarning(
                                    $"Device {device.Make} {device.Model} {device.SerialNumber} tag was re-enabled for sync after Corp ID removal. " +
                                    $"Aborting unsync; ConfirmSync will reconcile Corp ID on next run.",
                                    fullMethodName);
                            }
                            else
                            {
                                // Tag is still disabled — proceed with the unsync against the fresh device.
                                freshDevice.Status = DeviceStatus.NotSyncing;
                                freshDevice.CorporateIdentityID = string.Empty;
                                freshDevice.CorporateIdentity = string.Empty;
                                freshDevice.LastCorpIdentitySync = DateTime.UtcNow;

                                try
                                {
                                    await _dbService.UpdateDevice(freshDevice);
                                    updatedCount++;
                                    if (deletedCorpID) corpIDsRemoved++;
                                }
                                catch (Exception retryEx)
                                {
                                    _logger.DSLogException(
                                        $"Retry update failed for device {device.Make} {device.Model} {device.SerialNumber} after 412. " +
                                        $"Corp ID is removed from Graph but device row still shows Synced — " +
                                        $"ConfirmSync will reconcile on next run.",
                                        retryEx, fullMethodName);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Don't count toward ReleaseCorpIDs — can't confirm we own the outcome
                        _logger.DSLogException($"Failed to update device {device.Make} {device.Model} {device.SerialNumber} after Corp ID removal. Not releasing capacity for this device.", ex, fullMethodName);
                    }
                }
                else
                {
                    failureCount++;
                    _logger.DSLogError($"Corp ID removal from Graph failed for device {device.Make} {device.Model} {device.SerialNumber}. Device status not changed.", fullMethodName);
                }
            }

            _logger.DSLogInformation($"Section 1 complete. Updated {updatedCount} devices, removed {corpIDsRemoved} Corp IDs from Graph. Failures: {failureCount}.", fullMethodName);

            if (corpIDsRemoved > 0)
            {
                try
                {
                    CorpIdCapacityManager capacityManager = new CorpIdCapacityManager(_dbService, _logger, _MaxCorpIDsAllowed);
                    int available = await capacityManager.ReleaseCorpIDs(corpIDsRemoved, CancellationToken.None);

                    _logger.DSLogInformation($"Released {corpIDsRemoved} from Capacity Manager.  {available} CorpIDs remain.", fullMethodName);
                }
                catch (Exception ex)
                {
                    _logger.DSLogException("Failed to update CorpIDCounter after Section 1 removals.", ex, fullMethodName);
                }
            }

            return corpIDsRemoved;
        }

        private async Task<int> AddNotSyncingDevicesInEnabledTagsAsync(int batchSize)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("--- Section 2: Adding Corp IDs for NotSyncing devices in enabled tags. ---", fullMethodName);

            List<string> tagsWithSyncEnabled = await _dbService.GetSyncingDeviceTags();
            _logger.DSLogInformation($"Found {tagsWithSyncEnabled.Count} tags with sync enabled.", fullMethodName);

            if (tagsWithSyncEnabled.Count == 0)
            {
                _logger.DSLogInformation("No tags with sync enabled. Skipping Section 2.", fullMethodName);
                return 0;
            }

            var capacityManager = new CorpIdCapacityManager(_dbService, _logger, _MaxCorpIDsAllowed);

            int availableSlots;
            try
            {
                availableSlots = await capacityManager.GetAvailableCorpIDCount(CancellationToken.None);
                _logger.DSLogInformation($"Available Corp ID slots: {availableSlots}.", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Exiting.  Failed to retrieve available Corp ID count.", ex, fullMethodName);
                return 0;
            }

            if (availableSlots <= 0)
            {
                _logger.DSLogWarning("No available Corp ID slots. Skipping Section 2.", fullMethodName);
                return 0;
            }

            int effectiveBatchSize = Math.Min(batchSize, availableSlots);
            _logger.DSLogInformation($"Effective batch size for Section 2: {effectiveBatchSize} (min of batchSize {batchSize} and available slots {availableSlots}).", fullMethodName);

            List<Device> devicesToSync = await _dbService.GetNotSyncingDevicesInTags(tagsWithSyncEnabled, effectiveBatchSize);

            _logger.DSLogInformation($"Found {devicesToSync.Count} NotSyncing devices in enabled tags to add.", fullMethodName);


            if (devicesToSync.Count == 0)
            {
                _logger.DSLogInformation("No NotSyncing devices in enabled tags found. Skipping Section 2 processing.", fullMethodName);
                return 0;
            }

            // Reserve slots atomically — returned value may be less than requested if capacity tightened since the availability check
            int actualReserved;
            try
            {
                actualReserved = await capacityManager.ReserveCorpIDs(devicesToSync.Count, CancellationToken.None);
                _logger.DSLogInformation($"Requested {devicesToSync.Count} slots, reserved {actualReserved}.", fullMethodName);
            }
            catch(Exception ex)
            {
                _logger.DSLogException("Exiting. Failed to reserve Corp ID slots.", ex, fullMethodName);
                return 0;
            }

            if (actualReserved == 0)
            {
                _logger.DSLogWarning("No Corp ID slots could be reserved.", fullMethodName);
                return 0;
            }

            // Trim the work list if fewer slots were granted than devices found
            if (actualReserved < devicesToSync.Count)
            {
                _logger.DSLogInformation($"Trimming device list from {devicesToSync.Count} to {actualReserved} due to available capacity.", fullMethodName);
                devicesToSync = devicesToSync.Take(actualReserved).ToList();
            }

            int addedCount = 0;

            foreach (Device device in devicesToSync)
            {
                _logger.DSLogInformation($"-----Adding Corp ID for {device.Make} {device.Model} {device.SerialNumber}.-----", fullMethodName);

                if (device.OS == null)
                {
                    device.OS = DeviceOS.Unknown;
                }

                string identifier;
                if (device.OS == DeviceOS.Windows || device.OS == DeviceOS.Unknown)
                {
                    string escapedMake = "\"" + device.Make + "\"";
                    string escapedModel = "\"" + device.Model + "\"";
                    identifier = $"{escapedMake},{escapedModel},{device.SerialNumber}";
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
                    device.Status = DeviceStatus.Synced;
                    device.LastCorpIdentitySync = DateTime.UtcNow;
                    device.CorpIDFailureCount = 0;
                    addedCount++;

                    _logger.DSLogInformation($"Added Corp ID for device {device.Make} {device.Model} {device.SerialNumber}.", fullMethodName);
                }
                catch (Exception ex)
                {
                    _logger.DSLogException($"Error adding Corp ID for device {device.Make} {device.Model} {device.SerialNumber}: ", ex, fullMethodName);
                    // Reset to Added so AddNewDevices will retry
                    device.CorporateIdentityID = string.Empty;
                    device.Status = DeviceStatus.Added;

                    //not checking against max here since this should only ever be the first failure
                    device.CorpIDFailureCount++;
                    device.LastCorpIdentitySync = DateTime.MinValue;
                }

                bool graphAddSucceeded = !string.IsNullOrEmpty(device.CorporateIdentityID);

                try
                {
                    await _dbService.UpdateDevice(device);
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.DSLogWarning($"Device {device.Make} {device.Model} {device.SerialNumber} was deleted during processing.", fullMethodName);

                    if (graphAddSucceeded)
                    {
                        if (await TryDeleteCorpIdAsync(device.CorporateIdentityID, "rollback for deleted device", fullMethodName))
                        {
                            addedCount--; // Don't count toward CommitCorpIDCount
                        }
                    }
                    // If Graph add failed, nothing to roll back — device is gone anyway
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
                {
                    // Singleton prevents another ReconcileSyncState instance from racing us.
                    // No other automated function writes to NotSyncing rows (AddNewDevices handles Added,
                    // ConfirmSync handles Synced). Legitimate concurrent writers here are DeviceDeletion
                    // (after a user marks Deleting) or direct user UI edits.
                    _logger.DSLogWarning($"Device {device.Make} {device.Model} {device.SerialNumber} was modified concurrently during Corp ID add.", fullMethodName);

                    if (!graphAddSucceeded)
                    {
                        // Nothing in Graph to reconcile.
                        continue;
                    }

                    Device? freshDevice;
                    try
                    {
                        freshDevice = await _dbService.GetDevice(device.Id, device.PartitionKey);
                    }
                    catch (Exception readEx)
                    {
                        _logger.DSLogException(
                            $"Failed to re-read device {device.Make} {device.Model} {device.SerialNumber} after 412. Rolling back Corp ID {device.CorporateIdentityID}.",
                            readEx, fullMethodName);

                        if (await TryDeleteCorpIdAsync(device.CorporateIdentityID, "rollback after failed re-read on 412", fullMethodName))
                        {
                            addedCount--;
                        }
                        continue;
                    }

                    if (freshDevice is null || freshDevice.Status == DeviceStatus.Deleting)
                    {
                        // User marked it for deletion (or DeviceDeletion already removed it).
                        // Roll back the Corp ID we just added.
                        string state = freshDevice is null ? "deleted" : "Deleting";
                        _logger.DSLogInformation(
                            $"Device {device.Make} {device.Model} {device.SerialNumber} is {state}. Rolling back Corp ID {device.CorporateIdentityID}.",
                            fullMethodName);

                        if (await TryDeleteCorpIdAsync(device.CorporateIdentityID, $"rollback for {state} device", fullMethodName))
                        {
                            addedCount--;
                        }
                    }
                    else
                    {
                        // Defensive fallback: no known code path reaches here given current UI constraints
                        // (user can only mark Deleting). Roll back the Corp ID to be safe — if the device
                        // still needs syncing, ReconcileSyncState will pick it up again on the next run.
                        _logger.DSLogError(
                            $"Device {device.Make} {device.Model} {device.SerialNumber} in unexpected state '{freshDevice.Status}' after 412. " +
                            $"Rolling back Corp ID {device.CorporateIdentityID} as a precaution.",
                            fullMethodName);

                        if (await TryDeleteCorpIdAsync(device.CorporateIdentityID, "unexpected state after 412", fullMethodName))
                        {
                            addedCount--;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.DSLogException(
                        $"Unexpected error updating device {device.Make} {device.Model} {device.SerialNumber} after Corp ID add.",
                        ex, fullMethodName);

                    if (graphAddSucceeded)
                    {
                        _logger.DSLogInformation(
                            $"Rolling back Corp ID {device.CorporateIdentityID} due to unexpected update failure.",
                            fullMethodName);

                        if (await TryDeleteCorpIdAsync(device.CorporateIdentityID, "rollback after unexpected update failure", fullMethodName))
                        {
                            addedCount--;
                        }
                    }
                }
            }

            // Commit: moves actualReserved out of CorpIDReserve and records addedCount in CorpIDCount.
            // Any slots reserved but not added (failures) are released automatically by CommitCorpIDCount.
            try
            {
                int available = await capacityManager.CommitCorpIDCount(actualReserved, addedCount, CancellationToken.None);
                _logger.DSLogInformation($"Section 2 complete. Committed {addedCount} Corp IDs. Released {actualReserved - addedCount} unused reserved slots. {available} CorpIDs now available.", fullMethodName);
            }
            catch(Exception ex)
            {
                _logger.DSLogException("Failed to commit Corp ID count after additions. Manual resolution may be required.", ex, fullMethodName);
            }

            return addedCount;
        }

        /// <summary>
        /// Deletes a Corp ID from Graph and logs the result. Returns true if the Corp ID is confirmed
        /// removed (Success or NotFound), false if removal failed.
        /// </summary>
        private async Task<bool> TryDeleteCorpIdAsync(string corpId, string contextDescription, string fullMethodName)
        {
            if (string.IsNullOrEmpty(corpId)) return true;

            try
            {
                var result = await _graphBetaService.DeleteCorporateIdentifier(corpId);
                if (result == DeleteCorpIdResult.Success || result == DeleteCorpIdResult.NotFound)
                {
                    _logger.DSLogInformation($"Removed Corp ID {corpId} from Graph ({contextDescription}).", fullMethodName);
                    return true;
                }

                _logger.DSLogError($"Failed to remove Corp ID {corpId} from Graph ({contextDescription}). Manual cleanup may be required.", fullMethodName);
                return false;
            }
            catch (Exception ex)
            {
                _logger.DSLogException($"Exception removing Corp ID {corpId} from Graph ({contextDescription}).", ex, fullMethodName);
                return false;
            }
        }
    }
}
