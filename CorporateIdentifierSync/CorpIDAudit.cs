using CorporateIdentifierSync.Interfaces;
using DelegationStationShared;
using DelegationStationShared.Models;
using DelegationStationShared.Extensions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace CorporateIdentifierSync
{
    public class CorpIDAudit
    {
        private readonly ILogger<CorpIDAudit> _logger;
        private readonly ICosmosDbService _dbService;
        private readonly IGraphBetaService _graphBetaService;

        private int _MaxCorpIDsAllowed;
        private int _WarningThresholdPercent;

        public CorpIDAudit(
            ILoggerFactory loggerFactory,
            ICosmosDbService dbService,
            IGraphBetaService graphBetaService)
        {
            _logger = loggerFactory.CreateLogger<CorpIDAudit>();
            _dbService = dbService;
            _graphBetaService = graphBetaService;
        }

        public void GetEnvironmentVariables()
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

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

            //
            // Get warning threshold percentage
            //
            _WarningThresholdPercent = 90;
            string thresholdString = Environment.GetEnvironmentVariable("CORPID_WARNING_THRESHOLD_PERCENT");
            if (!int.TryParse(thresholdString, out int threshold))
            {
                _logger.DSLogError($"CORPID_WARNING_THRESHOLD_PERCENT is not set or invalid. Using default value: {_WarningThresholdPercent}.", fullMethodName);
            }
            else
            {
                // Clamp to 1-100 range
                _WarningThresholdPercent = Math.Max(1, Math.Min(100, threshold));
                if (threshold != _WarningThresholdPercent)
                {
                    _logger.DSLogWarning($"CORPID_WARNING_THRESHOLD_PERCENT value {threshold} was clamped to valid range. Using: {_WarningThresholdPercent}.", fullMethodName);
                }
                else
                {
                    _logger.DSLogInformation($"Corporate Identifier warning threshold set to: {_WarningThresholdPercent}%.", fullMethodName);
                }
            }
        }

        [Function("CorpIDAudit")]
        public async Task Run([TimerTrigger("%CorpIDAuditTriggerTime%")] TimerInfo myTimer)
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            try
            {
                _logger.DSLogInformation("C# Timer trigger function executed at: " + DateTime.Now, fullMethodName);
                if (myTimer.ScheduleStatus is not null)
                {
                    _logger.DSLogInformation("Next timer schedule at: " + myTimer.ScheduleStatus.Next, fullMethodName);
                }

                GetEnvironmentVariables();

                //
                // Retrieve all necessary counts
                //
                _logger.DSLogInformation("Retrieving Corporate Identifier counts for audit...", fullMethodName);

                int graphCount = await _graphBetaService.GetCorporateDeviceIdentifierCountAsync();
                int syncedDeviceCount = await _dbService.GetSyncedDeviceCountAsync();
                CorpIDCounter counter = await _dbService.GetCorpIDCounter();

                //
                // Log summary header
                //
                _logger.DSLogInformation("=====Corporate Identifier Audit Summary=====", fullMethodName);
                _logger.DSLogInformation($"Maximum Corporate Identifiers:      {_MaxCorpIDsAllowed}", fullMethodName);
                _logger.DSLogInformation($"Corporate Identifiers in Intune:    {graphCount}", fullMethodName);
                _logger.DSLogInformation($"Devices in Synced state:            {syncedDeviceCount}", fullMethodName);
                _logger.DSLogInformation($"DS CorpIDCount:                     {counter.CorpIDCount}", fullMethodName);
                _logger.DSLogInformation($"DS CorpIDCount(reserved):           {counter.CorpIDReserve}", fullMethodName);
                _logger.DSLogInformation("============================================", fullMethodName);
                _logger.DSLogInformation("Comparing data....", fullMethodName);

                //
                // Comparison 1: Graph count vs. Maximum
                //
                int warningThreshold = (_MaxCorpIDsAllowed * _WarningThresholdPercent) / 100;
                if (graphCount >= _MaxCorpIDsAllowed)
                {
                    _logger.DSLogError($"Corporate Identifiers in Intune ({graphCount}) are at or above maximum of {_MaxCorpIDsAllowed}.", fullMethodName);
                }
                else if (graphCount >= warningThreshold)
                {
                    _logger.DSLogWarning($"Corporate Identifiers in Intune ({graphCount}) are at or above {_WarningThresholdPercent}% threshold ({warningThreshold}) of maximum {_MaxCorpIDsAllowed}.", fullMethodName);
                }
                else
                {
                    _logger.DSLogInformation($"Corporate Identifiers in Intune ({graphCount}) are less than maximum of {_MaxCorpIDsAllowed}.", fullMethodName);
                    _logger.DSLogInformation($"Corporate Identifiers still available:  {_MaxCorpIDsAllowed - graphCount}", fullMethodName);
                }

                //
                // Comparison 2: Synced devices vs. Graph count
                //
                if (graphCount > syncedDeviceCount)
                {
                    _logger.DSLogError($"Corporate Identifiers in Intune ({graphCount}) are higher than synced devices in DB ({syncedDeviceCount}).", fullMethodName);
                }
                else if (graphCount < syncedDeviceCount)
                {
                    _logger.DSLogWarning($"Corporate Identifiers in Intune ({graphCount}) are lower than synced devices in DB ({syncedDeviceCount}).", fullMethodName);
                }
                else
                {
                    _logger.DSLogInformation($"Count of synced devices in DB ({syncedDeviceCount}) is the same as the number in Intune ({graphCount}).", fullMethodName);
                }

                //
                // Comparison 3: Counter CorpIDCount vs. Graph count
                //
                if (graphCount > counter.CorpIDCount)
                {
                    _logger.DSLogError($"Corporate Identifiers in Intune ({graphCount}) are higher than DS CorpIDCount ({counter.CorpIDCount}).", fullMethodName);
                }
                else if (graphCount < counter.CorpIDCount)
                {
                    _logger.DSLogWarning($"Corporate Identifiers in Intune ({graphCount}) are lower than DS CorpIDCount ({counter.CorpIDCount}).", fullMethodName);
                }
                else
                {
                    _logger.DSLogInformation($"DS CorpIDCount ({counter.CorpIDCount}) is the same as the number in Intune ({graphCount}).", fullMethodName);
                }

                //
                // Comparison 4: Counter Total (CorpIDCount + Reserve) vs. Graph count
                //
                int counterTotal = counter.GetTotal();
                if (graphCount > counterTotal)
                {
                    _logger.DSLogError($"Corporate Identifiers in Intune ({graphCount}) are higher than DS total count (CorpIDCount + Reserved = {counterTotal}).", fullMethodName);
                }
                else if (graphCount < counterTotal)
                {
                    _logger.DSLogWarning($"Corporate Identifiers in Intune ({graphCount}) are lower than DS total count (CorpIDCount + Reserved = {counterTotal}).", fullMethodName);
                }
                else
                {
                    _logger.DSLogInformation($"DS total count (CorpIDCount + Reserved = {counterTotal}) is the same as the number in Intune ({graphCount}).", fullMethodName);
                }

                //
                // Comparison 5: Counter CorpIDCount vs. Synced devices
                //
                if (counter.CorpIDCount != syncedDeviceCount)
                {
                    _logger.DSLogWarning($"DS CorpIDCount ({counter.CorpIDCount}) is different than synced devices in DB ({syncedDeviceCount}).", fullMethodName);
                }
                else
                {
                    _logger.DSLogInformation($"DS CorpIDCount ({counter.CorpIDCount}) is the same as synced devices in DB ({syncedDeviceCount}).", fullMethodName);
                }

                //
                // Comparison 6: Counter Total (CorpIDCount + Reserve) vs. Synced devices
                //
                if (counterTotal != syncedDeviceCount)
                {
                    _logger.DSLogError($"DS total count (CorpIDCount + Reserved = {counterTotal}) is different than synced devices in DB ({syncedDeviceCount}).", fullMethodName);
                }
                else
                {
                    _logger.DSLogInformation($"DS total count (CorpIDCount + Reserved = {counterTotal}) is the same as synced devices in DB ({syncedDeviceCount}).", fullMethodName);
                }

                _logger.DSLogInformation("Corporate Identifier audit completed successfully.", fullMethodName);
            }
            catch (Exception ex)
            {
                _logger.DSLogException("Corporate Identifier audit failed with an exception.", ex, fullMethodName);
            }
        }
    }
}
