using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DelegationStationShared;
using DelegationStationShared.Extensions;
using DelegationStationShared.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using UpdateDevices.Interfaces;

namespace UpdateDevices
{
    public class Cleanup
    {
        private readonly ILogger _logger;
        private readonly ICosmosDbService _dbService;
        private int _maxUDAttempts;

        public Cleanup(ILoggerFactory loggerFactory, ICosmosDbService dbService)
        {
            _logger = loggerFactory.CreateLogger<Cleanup>();
            _dbService = dbService;

            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            bool parseConfig = int.TryParse(Environment.GetEnvironmentVariable("MaxUpdateDeviceAttempts", EnvironmentVariableTarget.Process), out _maxUDAttempts);
            if (!parseConfig)
            {
                _maxUDAttempts = 5;
                _logger.DSLogWarning("MaxUpdateDeviceAttempts environment variable not set. Defaulting to 5 attempts.", fullMethodName);
            }
        }

        [Function("Cleanup")]
        public async Task Run([TimerTrigger("%CleanupTriggerTime%")] TimerInfo myTimer)
        {

            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            if (myTimer.ScheduleStatus is not null)
            {
                _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}");
            }

            await CleanupUDProcessedEntries();
        }

        private async Task CleanupUDProcessedEntries()
        {
            string methodName = ExtensionHelper.GetMethodName() ?? "";
            string className = this.GetType().Name;
            string fullMethodName = className + "." + methodName;

            _logger.DSLogInformation("Cleaning up old straggler entries...", fullMethodName);


            // Cleanup entries from devices that eventually matched in UDA
            List<Straggler> stragglers = await _dbService.GetStragglersProcessedByUD(_maxUDAttempts);
            foreach (Straggler straggler in stragglers)
            {

                await _dbService.DeleteStraggler(straggler);
                _logger.DSLogInformation("Deleted Straggler entry for device " + straggler.ManagedDeviceID + " (count=" + straggler.UDAttemptCount +
                                         " , Last updated by UD=" + straggler.LastUDUpdateDateTime + ")", fullMethodName);
            }
        }
    }
}
