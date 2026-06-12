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
    public class ConfirmSync
    {
        private readonly ILogger _logger;
        private readonly ICosmosDbService _dbService;
        private readonly IGraphBetaService _graphBetaService;
        private readonly IFunctionSingletonLock _singletonLock;

        private bool _IsCorpIDSyncEnabled;
        private int _SyncIntervalHours;
        private int _MaxCorpIDsAllowed;

        public ConfirmSync(
            ILoggerFactory loggerFactory,
            ICosmosDbService dbService,
            IGraphBetaService graphBetaService,
            IFunctionSingletonLock singletonLock)
        {
            _logger = loggerFactory.CreateLogger<ConfirmSync>();
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
            // Get sync interval hours
            //
            _SyncIntervalHours = 0;
            if (!int.TryParse(Environment.GetEnvironmentVariable("SyncIntervalHours"), out _SyncIntervalHours))
            {
                _logger.DSLogError("SyncIntervalHours is not set or not a valid integer. Defaulting to 0.", fullMethodName);
            }
            else
            {
                _logger.DSLogInformation($"Using SyncIntervalHours: {_SyncIntervalHours}.", fullMethodName);
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

        [Function("ConfirmSync")]
        public async Task Run([TimerTrigger("%ConfirmSyncTriggerTime%")] TimerInfo myTimer)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            await using var handle = await _singletonLock.TryAcquireAsync(nameof(ConfirmSync));
            if (handle is null)
            {
                _logger.DSLogWarning("Another instance of ConfirmSync is already running. Exiting.", fullMethodName);
                return;
            }

            _logger.DSLogInformation($"C# Timer trigger function executed at: {DateTime.Now}", fullMethodName);

            if (myTimer.ScheduleStatus is not null)
            {
                _logger.DSLogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}", fullMethodName);
            }

            GetEnvironmentVariables();

            //
            //  We don't need to keep going if syncing is disabled
            //
            if (!_IsCorpIDSyncEnabled)
            {
                _logger.DSLogInformation("Syncing not enabled.  No work to do.  Function is exiting.", fullMethodName);
                return;
            }

            //
            // Validate sync interval. If invalid (0 from failed parse), log and exit.
            //
            if (_SyncIntervalHours < 0)
            {
                _logger.DSLogError("SyncIntervalHours is not set or invalid. Exiting.", fullMethodName);
                return;
            }
            _logger.DSLogInformation($"Checking devices last synced over {_SyncIntervalHours} hours ago.", fullMethodName);

            //
            // Only checking devices that are in tags with sync enabled.
            // Get list of tags with sync enabled. If none found, log and exit.
            //
            List<string> tagsWithSyncEnabled = await _dbService.GetSyncingDeviceTags();
            if (tagsWithSyncEnabled.Count == 0)
            {
                _logger.DSLogInformation("No tags with sync enabled found. No work to do. Function is exiting.", fullMethodName);
                return;
            }

            //
            // Get all devices that were synced before the cutoff time, and filter to only those in sync-enabled tags.
            //
            List<Device> candidates = await _dbService.GetSyncedDevicesSyncedBefore(DateTime.UtcNow.AddHours(-_SyncIntervalHours));
            List<Device> devicesToCheck = candidates
                .Where(d => d.Tags.Count > 0 && tagsWithSyncEnabled.Contains(d.Tags[0]))
                .ToList();

            _logger.DSLogInformation($"Found {devicesToCheck.Count} Synced devices in sync-enabled tags to confirm.", fullMethodName);

            //
            //  Track the count of devices in various states for logging
            //
            int countCorpIDsFound = 0;
            int countCorpIDsReAdded = 0;
            int countCorpIDsReAddFailed = 0;

            foreach (Device device in devicesToCheck)
            {
                _logger.DSLogInformation($"-----Confirming corporate identifier for {device.Make} {device.Model} {device.SerialNumber}.-----", fullMethodName);

                // Boolean used to keep track of scenarios
                bool corpIDFound = false;
                bool corpIDReAdded = false;
                bool corpIDReAddFailed = false;

                // Check whether the Corp ID still exists in Graph
                if (!string.IsNullOrEmpty(device.CorporateIdentityID))
                {
                    try
                    {
                    corpIDFound = await _graphBetaService.CorporateIdentifierExists(device.CorporateIdentityID);
                }
                    catch (Exception ex)
                    {
                        _logger.DSLogException(
                            $"Error checking Corp ID existence for {device.Make} {device.Model} {device.SerialNumber}. Skipping device this run.",
                            ex, fullMethodName);
                        continue; // leave device untouched; next ConfirmSync run will retry
                    }
                }
                else
                {
                    _logger.DSLogWarning("Device does not have a CorporateIdentityID stored in DB. Not sure how we got here. Will attempt to re-add.", fullMethodName);
                }

                if (corpIDFound)
                {
                    _logger.DSLogInformation("Corporate Identifier confirmed present.", fullMethodName);
                    countCorpIDsFound++;
                    device.LastCorpIdentitySync = DateTime.UtcNow;
                }
                else
                {
                    // Re-add the missing Corp ID
                    _logger.DSLogInformation("Corporate Identifier not found, re-adding.", fullMethodName);

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
                        device.LastCorpIdentitySync = DateTime.UtcNow;
                        device.Status = DeviceStatus.Synced;
                        device.CorpIDFailureCount = 0;
                        corpIDReAdded = true;
                        countCorpIDsReAdded++;
                    }
                    catch (Exception ex)
                    {
                        _logger.DSLogException($"Error re-adding corporate identifier for device {device.Make} {device.Model} {device.SerialNumber}: ", ex, fullMethodName);
                        // Reset to Added so AddNewDevices will retry
                        device.CorporateIdentityID = string.Empty;

                        // Not checking against max since this should only ever be the first failure
                        device.CorpIDFailureCount++;
                        device.Status = DeviceStatus.Added;
                        corpIDReAddFailed = true;
                        countCorpIDsReAddFailed++;
                    }
                }

                //
                //  Update device entry
                //
                try
                {
                    await _dbService.UpdateDevice(device);
                    _logger.DSLogInformation($"Updated device {device.Make} {device.Model} {device.SerialNumber} in Delegation Station.", fullMethodName);
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Device was deleted between our read and write (DeviceDeletion or user UI).
                    _logger.DSLogWarning(
                        $"Device {device.Make} {device.Model} {device.SerialNumber} was deleted during ConfirmSync.",
                        fullMethodName);

                    if (corpIDFound)
                    {
                        // Only a timestamp update was lost — nothing to undo.
                        countCorpIDsFound--;
                    }
                    else if (corpIDReAddFailed)
                    {
                        // No Corp ID was actually added — counter-only adjustment for the summary log.
                        countCorpIDsReAddFailed--;
                    }
                    else if (corpIDReAdded)
                    {
                        // We added a Corp ID to Graph but the device is gone — roll back.
                        await RollbackReAddedCorpIdAsync(device.CorporateIdentityID, fullMethodName);
                        countCorpIDsReAdded--;
                    }
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
                {
                    // Singleton prevents another ConfirmSync instance from racing us.
                    // Legitimate concurrent writers on a Synced row: user marking Deleting via UI,
                    // or ReconcileSyncState flipping it to NotSyncing after a tag sync-setting change.
                    if (corpIDFound)
                    {
                        // Only a timestamp update was lost.
                        _logger.DSLogWarning(
                            $"Device {device.Make} {device.Model} {device.SerialNumber} was modified concurrently. " +
                            $"Corp ID already confirmed; no action needed.",
                            fullMethodName);
                        countCorpIDsFound--;
                    }
                    else if (corpIDReAddFailed)
                    {
                        countCorpIDsReAddFailed--;
                    }
                    else if (corpIDReAdded)
                    {
                        _logger.DSLogWarning(
                            $"Device {device.Make} {device.Model} {device.SerialNumber} was modified concurrently after Corp ID re-add. " +
                            $"Reading fresh state to determine rollback.",
                            fullMethodName);

                        Device? freshDevice;
                        try
                        {
                            freshDevice = await _dbService.GetDevice(device.Id, device.PartitionKey);
                        }
                        catch (Exception readEx)
                        {
                            _logger.DSLogException(
                                $"Could not read fresh device {device.Make} {device.Model} {device.SerialNumber} to determine rollback. " +
                                $"Leaving Corp ID {device.CorporateIdentityID} in Graph; ReconcileSyncState will reconcile.",
                                readEx, fullMethodName);
                            // Don't decrement — Corp ID is still in Graph and tracked.
                            continue;
                        }

                        if (freshDevice is null ||
                            freshDevice.Status == DeviceStatus.Deleting ||
                            freshDevice.Status == DeviceStatus.NotSyncing)
                        {
                            _logger.DSLogWarning(
                                $"Device {device.Make} {device.Model} {device.SerialNumber} is {(freshDevice?.Status.ToString() ?? "deleted")}. " +
                                $"Rolling back re-added Corp ID.",
                                fullMethodName);
                            await RollbackReAddedCorpIdAsync(device.CorporateIdentityID, fullMethodName);
                            countCorpIDsReAdded--;
                        }
                        else
                        {
                            // Singleton makes a second ConfirmSync impossible, and no other function transitions
                            // a row into a state that leaves our Corp ID valid here. This is unexpected.
                            // Leave the Corp ID in Graph; ReconcileSyncState/the next ConfirmSync run will reconcile.
                            _logger.DSLogWarning(
                                $"Device {device.Make} {device.Model} {device.SerialNumber} in unexpected state '{freshDevice.Status}' after PreconditionFailed. " +
                                $"Leaving Corp ID {device.CorporateIdentityID} in Graph for downstream reconciliation.",
                                fullMethodName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.DSLogException(
                        $"Failed to update device record {device.Make} {device.Model} {device.SerialNumber}. " +
                        $"CorporateIdentifier details may be out of sync.",
                        ex, fullMethodName);

                    // The DB row was not updated, so its on-disk state is unchanged.
                    // Undo the in-memory counter bumps so the post-loop capacity reconciliation
                    // reflects what actually persisted.
                    if (corpIDReAddFailed)
                    {
                        // Because DB wasn't updated, don't release even though we know CorpID is missing
                        // we want to just leave it like we found it and let ConfirmSync handle it on the next run
                        countCorpIDsReAddFailed--;
                    }
                    else if (corpIDFound)
                    {
                        // Only a timestamp update was lost — counter-only adjustment for the summary log.
                        countCorpIDsFound--;
                    }
                    else if (corpIDReAdded)
                    {
                        // A new Corp ID is in Graph but the DB row wasn't updated to point at it.
                        // Leave countCorpIDsReAdded as-is: the orphan still consumes a slot in Graph,
                        // and ReconcileSyncState / the next ConfirmSync run will reconcile.
                        _logger.DSLogWarning(
                            $"Device {device.Make} {device.Model} {device.SerialNumber} re-added Corp ID {device.CorporateIdentityID} but DB update failed. " +
                            $"Corp ID left in Graph for downstream reconciliation.",
                            fullMethodName);
                    }
                }
            }

            _logger.DSLogInformation($"ConfirmSync completed. Processed {countCorpIDsFound + countCorpIDsReAdded + countCorpIDsReAddFailed} devices: {countCorpIDsFound} found, {countCorpIDsReAdded} re-added, " +
                                     $"{countCorpIDsReAddFailed} failed to re-add.", fullMethodName);

            //
            // Update CorpID counter for failed re-adds
            //
            if (countCorpIDsReAddFailed > 0)
            {
                var capacityManager = new CorpIdCapacityManager(_dbService, _logger, _MaxCorpIDsAllowed);
                try
                {
                    await capacityManager.ReleaseCorpIDs(countCorpIDsReAddFailed, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.DSLogException($"Failed to release {countCorpIDsReAddFailed} CorpID slots for failed re-adds. Manual correction may be required.", ex, fullMethodName);
                }
            }
        }

        private async Task RollbackReAddedCorpIdAsync(string corpId, string fullMethodName)
        {
            if (string.IsNullOrEmpty(corpId)) return;

            var rollbackResult = await _graphBetaService.DeleteCorporateIdentifier(corpId);
            if (rollbackResult == DeleteCorpIdResult.Error)
            {
                _logger.DSLogError($"Failed to roll back Corp ID {corpId}. Manual cleanup required.", fullMethodName);
            }
            else
            {
                // Success or NotFound — Corp ID is confirmed gone from Graph
                _logger.DSLogInformation($"Rolled back re-added Corp ID {corpId}.", fullMethodName);
            }
        }
    }
}
