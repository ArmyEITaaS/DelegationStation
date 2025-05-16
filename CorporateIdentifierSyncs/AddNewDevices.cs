using CorporateIdentifierSync.Interfaces;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta.Models;
using DelegationStationShared.Extensions;
using Device = DelegationStationShared.Models.Device;
using DelegationStationShared;
using DelegationStationShared.Models;
using DelegationStationShared.Enums;

namespace CorporateIdentifierSync
{
    public class AddNewDevices
    {
        private readonly ILogger<AddNewDevices> _logger;
        private readonly ICosmosDbService _dbService;
        private readonly IGraphBetaService _graphBetaService;

        public AddNewDevices(ILoggerFactory loggerFactory, ICosmosDbService dbService, IGraphBetaService graphBetaService)
        {
            _logger = loggerFactory.CreateLogger<AddNewDevices>();
            _dbService = dbService;
            _graphBetaService = graphBetaService;

        }


        [Function("AddNewDevices")]
        public async Task Run([TimerTrigger("%AddDevicesTriggerTime%")] TimerInfo myTimer)
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
                _logger.DSLogError("EnableCorpIDSync not set or not a valid boolean. Disabling sync.", fullMethodName);
            }
            else if (!isCorpIDSyncEnabled)
            {
                _logger.DSLogInformation("EnableCorpIDSync set to false. Disabling sync.", fullMethodName);
            }

            if (!isCorpIDSyncEnabled)
            {
                _logger.DSLogInformation("Syncing not enabled.  No work to do.  Function is exiting.", fullMethodName);
                return;
            }



            // Get All Devices without Corporate Identifier values or fields
            List<Device> devicesToMigrate = new List<Device>();
            try
            {
                devicesToMigrate = await _dbService.GetAddedDevices();
                _logger.DSLogInformation($"Found {devicesToMigrate.Count} devices to migrate.", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogError($"Exiting. Error getting devices to migrate: {ex.Message}", fullMethodName);
                return;
            }


            // For each device set blank Corporate Identifier values
            int deviceCount = 0;
            foreach (Device device in devicesToMigrate)
            {

                // Set OS if not set
                if(device.OS == null)
                {
                    device.OS = DeviceOS.Unknown;
                }


                // Get Device Tag sync setting
                bool isCorpIDSyncEnabledForTag = false;
                if (device.Tags.Count == 0)
                {
                    _logger.DSLogError($"Device {device.Make} {device.Model} {device.SerialNumber} has no tags. Skipping.", fullMethodName);
                    continue;
                }
                else
                {
                    DeviceTag tag = await _dbService.GetDeviceTag(device.Tags[0]);
                    isCorpIDSyncEnabledForTag = tag.CorpIDSyncEnabled;
                }


                // Add the Corporate Identifier
                try
                {
                    if (isCorpIDSyncEnabledForTag)
                    {
                        string identifier = "";
                        if ((device.OS == DeviceOS.Windows) || (device.OS == DeviceOS.Unknown))
                        {
                            device.CorporateIdentityType = ImportedDeviceIdentityType.ManufacturerModelSerial;
                            _logger.DSLogInformation($"-----Adding Corporate Identifier for device {device.Make} {device.Model} {device.SerialNumber}.-----", fullMethodName);

                            // Putting make and model in quotes to handle commas
                            string escapedMake = "\"" + device.Make + "\"";
                            string escapedModel = "\"" + device.Model + "\"";
                            identifier = $"{escapedMake},{escapedModel},{device.SerialNumber}";
                        }
                        else
                        {
                            device.CorporateIdentityType = ImportedDeviceIdentityType.SerialNumber;
                            identifier = device.SerialNumber;
                        }
                        ImportedDeviceIdentity deviceIdentity = await _graphBetaService.AddCorporateIdentifier(device.CorporateIdentityType, identifier);

                        // Set the Corporate Identifier values
                        device.CorporateIdentityID = deviceIdentity.Id;
                        device.CorporateIdentity = deviceIdentity.ImportedDeviceIdentifier;
                        device.Status = DeviceStatus.Synced;
                        device.LastCorpIdentitySync = DateTime.UtcNow;

                    }
                    else
                    {
                        _logger.DSLogInformation($"Device {device.Make} {device.Model} {device.SerialNumber} tag {device.Tags[0]} is not enabled for sync.", fullMethodName);
                        device.Status = DeviceStatus.NotSyncing;
                        device.LastCorpIdentitySync = DateTime.UtcNow;
                    }

                    // Update the DB entry with the new Corporate Identifier info
                    await _dbService.UpdateDevice(device);
                    deviceCount++;

                    _logger.DSLogInformation($"Successfully added Corporate Identifier for device {device.Make} {device.Model} {device.SerialNumber}.", fullMethodName);
                }
                catch (Exception ex)
                {
                    _logger.DSLogError($"Error adding Corporate Identifier for device {device.Make} {device.Model} {device.SerialNumber}: {ex.Message}", fullMethodName);
                }
            }

            _logger.DSLogInformation($"Successfully migrated {deviceCount} devices.", fullMethodName);
        }
    }
}
