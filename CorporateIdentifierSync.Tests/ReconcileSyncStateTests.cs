using CorporateIdentifierSync.Enums;
using CorporateIdentifierSync.Interfaces;
using CorporateIdentifierSync.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta.Models;
using Xunit;
using Device = DelegationStationShared.Models.Device;
using DeviceTag = DelegationStationShared.Models.DeviceTag;

namespace CorporateIdentifierSync.Tests.ReconcileSyncStateTests
{
    [Collection("EnvVarTests")]
    public class ReconcileSyncStateTests
    {
        // -----------------------------------------------------------------------
        // Inner helpers
        // -----------------------------------------------------------------------

        /// <summary>Captures every log call so tests can assert on log output.</summary>
        private sealed class CapturingLogger : ILogger
        {
            public List<(LogLevel Level, string Message)> Logs { get; } = [];

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => Logs.Add((logLevel, formatter(state, exception)));

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }

        /// <summary>Returns a <see cref="CapturingLogger"/> for every category.</summary>
        private sealed class CapturingLoggerFactory : ILoggerFactory
        {
            public CapturingLogger Logger { get; } = new();
            public bool CreateLoggerCalled { get; private set; }

            public void AddProvider(ILoggerProvider provider) { }

            public ILogger CreateLogger(string categoryName)
            {
                CreateLoggerCalled = true;
                return Logger;
            }

            public void Dispose() { }
        }

        /// <summary>Minimal <see cref="IAsyncDisposable"/> used as a lock handle.</summary>
        private sealed class AsyncDisposableHandle : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        /// <summary>Stub for <see cref="IFunctionSingletonLock"/> that either acquires or rejects the lock.</summary>
        private sealed class StubSingletonLock : IFunctionSingletonLock
        {
            private readonly bool _acquireLock;

            public StubSingletonLock(bool acquireLock) => _acquireLock = acquireLock;

            public Task<IAsyncDisposable?> TryAcquireAsync(
                string lockName,
                CancellationToken cancellationToken = default)
                => Task.FromResult<IAsyncDisposable?>(_acquireLock ? new AsyncDisposableHandle() : null);
        }

        /// <summary>
        /// Configurable stub for <see cref="ICosmosDbService"/>.
        /// Methods needed by the paths under test return configurable values;
        /// all unused methods throw <see cref="NotImplementedException"/>.
        /// </summary>
        private sealed class StubCosmosDbService : ICosmosDbService
        {
            public List<string> NonSyncingTagsToReturn { get; set; } = [];
            public List<Device> SyncedDevicesInTagsToReturn { get; set; } = [];
            public List<string> SyncingTagsToReturn { get; set; } = [];
            public CorpIDCounter CounterToReturn { get; set; } = new CorpIDCounter(0);

            public Task<List<string>> GetNonSyncingDeviceTags()
                => Task.FromResult(NonSyncingTagsToReturn);

            public Task<List<Device>> GetSyncedDevicesInTags(List<string> tagIds, int batchSize)
                => Task.FromResult(SyncedDevicesInTagsToReturn);

            public Task<List<string>> GetSyncingDeviceTags()
                => Task.FromResult(SyncingTagsToReturn);

            public Task<CorpIDCounter> GetCorpIDCounter()
                => Task.FromResult(CounterToReturn);

            public Task<bool> TrySetCorpIDCounter(CorpIDCounter counter, string etag)
                => Task.FromResult(true);

            public Task<List<Device>> GetAddedDevices(int batchSize) => throw new NotImplementedException();
            public Task<List<Device>> GetAddedDevicesNotSyncing(List<string> tagIds, int batchSize) => throw new NotImplementedException();
            public Task<List<Device>> GetAddedDevicesToSync(List<string> tagIds, int batchSize) => throw new NotImplementedException();
            public Task<List<Device>> GetDevicesMarkedForDeletion() => throw new NotImplementedException();
            public Task UpdateDevice(Device device) => throw new NotImplementedException();
            public Task DeleteDevice(Device device) => throw new NotImplementedException();
            public Task<Device?> GetDevice(Guid id, string partitionKey) => throw new NotImplementedException();
            public Task<List<Device>> GetDevicesSyncedBefore(DateTime date) => throw new NotImplementedException();
            public Task<List<Device>> GetSyncedDevicesSyncedBefore(DateTime date) => throw new NotImplementedException();
            public Task<DeviceTag> GetDeviceTag(string id) => throw new NotImplementedException();
            public Task<List<Device>> GetNotSyncingDevicesInTags(List<string> tagsWithSyncEnabled, int batchSize) => throw new NotImplementedException();
        }

        /// <summary>Stub for <see cref="IGraphBetaService"/> — not called by the methods under test.</summary>
        private sealed class StubGraphBetaService : IGraphBetaService
        {
            public Task<ImportedDeviceIdentity> AddCorporateIdentifier(
                ImportedDeviceIdentityType type, string identifier)
                => throw new NotImplementedException();

            public Task<DeleteCorpIdResult> DeleteCorporateIdentifier(string identifierID)
                => throw new NotImplementedException();

            public Task<bool> CorporateIdentifierExists(string identiferID)
                => throw new NotImplementedException();

            public Task<int> GetCorporateDeviceIdentifierCountAsync()
                => throw new NotImplementedException();
        }

        /// <summary>
        /// Saves and restores a set of environment variables for test isolation.
        /// </summary>
        private sealed class EnvVarScope : IDisposable
        {
            private readonly Dictionary<string, string?> _saved = [];

            public void Set(string name, string? value)
            {
                if (!_saved.ContainsKey(name))
                {
                    _saved[name] = Environment.GetEnvironmentVariable(name);
                }

                Environment.SetEnvironmentVariable(name, value);
            }

            public void Dispose()
            {
                foreach (KeyValuePair<string, string?> entry in _saved)
                {
                    Environment.SetEnvironmentVariable(entry.Key, entry.Value);
                }
            }
        }

        // -----------------------------------------------------------------------
        // Factory helpers
        // -----------------------------------------------------------------------

        private static ReconcileSyncState CreateSut(
            CapturingLoggerFactory loggerFactory,
            StubCosmosDbService? db = null,
            bool acquireLock = true)
        {
            db ??= new StubCosmosDbService();
            return new ReconcileSyncState(
                loggerFactory,
                db,
                new StubGraphBetaService(),
                new StubSingletonLock(acquireLock));
        }

        private static TimerInfo MakeTimerInfo(bool withScheduleStatus)
        {
            var timer = new TimerInfo();
            if (withScheduleStatus)
            {
                timer.ScheduleStatus = new ScheduleStatus
                {
                    Next = DateTime.UtcNow.AddHours(1),
                };
            }

            return timer;
        }

        // -----------------------------------------------------------------------
        // Constructor tests
        // -----------------------------------------------------------------------

        /// <summary>
        /// Verifies that the constructor creates a valid ReconcileSyncState instance when all dependencies are provided.
        /// </summary>
        [Fact]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            var loggerFactory = new CapturingLoggerFactory();
            var db = new StubCosmosDbService();
            var graph = new StubGraphBetaService();
            var singletonLock = new StubSingletonLock(false);

            var sut = new ReconcileSyncState(loggerFactory, db, graph, singletonLock);

            Assert.NotNull(sut);
        }

        /// <summary>
        /// Verifies that the constructor calls CreateLogger on the provided logger factory.
        /// </summary>
        [Fact]
        public void Constructor_CallsCreateLoggerOnLoggerFactory()
        {
            var loggerFactory = new CapturingLoggerFactory();

            _ = CreateSut(loggerFactory);

            Assert.True(loggerFactory.CreateLoggerCalled);
        }

        // -----------------------------------------------------------------------
        // GetEnvironmentVariables – EnableCorpIDSync
        // -----------------------------------------------------------------------

        /// <summary>
        /// Verifies that GetEnvironmentVariables logs an error when the EnableCorpIDSync environment variable is not set.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_EnableCorpIDSyncNotSet_LogsError()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", null);
            env.Set("ReconcileSyncBatchSize", "100");
            env.Set("MAX_CORPIDS_ALLOWED", "5000");

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory);

            sut.GetEnvironmentVariables();

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Error && l.Message.Contains("EnableCorpIDSync not set or not a valid boolean"));
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables logs an error when EnableCorpIDSync is set to an invalid boolean string.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_EnableCorpIDSyncInvalid_LogsError()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "not-a-bool");
            env.Set("ReconcileSyncBatchSize", "100");
            env.Set("MAX_CORPIDS_ALLOWED", "5000");

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory);

            sut.GetEnvironmentVariables();

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Error && l.Message.Contains("EnableCorpIDSync not set or not a valid boolean"));
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables does not log a sync-flag error when EnableCorpIDSync is set to true.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_EnableCorpIDSyncTrue_NoSyncFlagError()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            env.Set("ReconcileSyncBatchSize", "100");
            env.Set("MAX_CORPIDS_ALLOWED", "5000");

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory);

            sut.GetEnvironmentVariables();

            Assert.DoesNotContain(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Error && l.Message.Contains("EnableCorpIDSync not set or not a valid boolean"));
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables does not log a sync-flag error when EnableCorpIDSync is set to false.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_EnableCorpIDSyncFalse_NoSyncFlagError()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "false");
            env.Set("ReconcileSyncBatchSize", "100");
            env.Set("MAX_CORPIDS_ALLOWED", "5000");

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory);

            sut.GetEnvironmentVariables();

            Assert.DoesNotContain(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Error && l.Message.Contains("EnableCorpIDSync not set or not a valid boolean"));
        }

        // -----------------------------------------------------------------------
        // GetEnvironmentVariables – ReconcileSyncBatchSize
        // -----------------------------------------------------------------------

        /// <summary>
        /// Verifies that GetEnvironmentVariables logs a warning when ReconcileSyncBatchSize is not set.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_BatchSizeNotSet_LogsWarning()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            env.Set("ReconcileSyncBatchSize", null);
            env.Set("MAX_CORPIDS_ALLOWED", "5000");

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory);

            sut.GetEnvironmentVariables();

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Warning && l.Message.Contains("ReconcileSyncBatchSize is not set or invalid"));
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables logs a warning when ReconcileSyncBatchSize is set to a non-numeric string.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_BatchSizeInvalidString_LogsWarning()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            env.Set("ReconcileSyncBatchSize", "not-a-number");
            env.Set("MAX_CORPIDS_ALLOWED", "5000");

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory);

            sut.GetEnvironmentVariables();

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Warning && l.Message.Contains("ReconcileSyncBatchSize is not set or invalid"));
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables logs a warning when ReconcileSyncBatchSize is set to zero.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_BatchSizeZero_LogsWarning()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            env.Set("ReconcileSyncBatchSize", "0");
            env.Set("MAX_CORPIDS_ALLOWED", "5000");

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory);

            sut.GetEnvironmentVariables();

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Warning && l.Message.Contains("ReconcileSyncBatchSize is not set or invalid"));
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables logs a warning when ReconcileSyncBatchSize is set to a negative value.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_BatchSizeNegative_LogsWarning()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            env.Set("ReconcileSyncBatchSize", "-5");
            env.Set("MAX_CORPIDS_ALLOWED", "5000");

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory);

            sut.GetEnvironmentVariables();

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Warning && l.Message.Contains("ReconcileSyncBatchSize is not set or invalid"));
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables logs the batch size at information level when ReconcileSyncBatchSize is valid.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_BatchSizeValid_LogsInformation()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            env.Set("ReconcileSyncBatchSize", "500");
            env.Set("MAX_CORPIDS_ALLOWED", "5000");

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory);

            sut.GetEnvironmentVariables();

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Information && l.Message.Contains("Using ReconcileSyncBatchSize: 500"));
        }

        // -----------------------------------------------------------------------
        // GetEnvironmentVariables – MAX_CORPIDS_ALLOWED
        // -----------------------------------------------------------------------

        /// <summary>
        /// Verifies that GetEnvironmentVariables logs an error when MAX_CORPIDS_ALLOWED is not set.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_MaxCorpIDsNotSet_LogsError()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            env.Set("ReconcileSyncBatchSize", "100");
            env.Set("MAX_CORPIDS_ALLOWED", null);

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory);

            sut.GetEnvironmentVariables();

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Error && l.Message.Contains("MAX_CORPIDS_ALLOWED is not set or invalid"));
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables logs an error when MAX_CORPIDS_ALLOWED is set to an invalid string.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_MaxCorpIDsInvalidString_LogsError()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            env.Set("ReconcileSyncBatchSize", "100");
            env.Set("MAX_CORPIDS_ALLOWED", "bad-value");

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory);

            sut.GetEnvironmentVariables();

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Error && l.Message.Contains("MAX_CORPIDS_ALLOWED is not set or invalid"));
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables logs an error when MAX_CORPIDS_ALLOWED is set to zero.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_MaxCorpIDsZero_LogsError()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            env.Set("ReconcileSyncBatchSize", "100");
            env.Set("MAX_CORPIDS_ALLOWED", "0");

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory);

            sut.GetEnvironmentVariables();

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Error && l.Message.Contains("MAX_CORPIDS_ALLOWED is not set or invalid"));
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables logs an error when MAX_CORPIDS_ALLOWED is set to a negative value.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_MaxCorpIDsNegative_LogsError()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            env.Set("ReconcileSyncBatchSize", "100");
            env.Set("MAX_CORPIDS_ALLOWED", "-10");

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory);

            sut.GetEnvironmentVariables();

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Error && l.Message.Contains("MAX_CORPIDS_ALLOWED is not set or invalid"));
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables logs the max corp IDs at information level when MAX_CORPIDS_ALLOWED is valid.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_MaxCorpIDsValid_LogsInformation()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            env.Set("ReconcileSyncBatchSize", "100");
            env.Set("MAX_CORPIDS_ALLOWED", "5000");

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory);

            sut.GetEnvironmentVariables();

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Information && l.Message.Contains("Maximum allowed Corporate Identifiers for the tenant is set to: 5000"));
        }

        // -----------------------------------------------------------------------
        // Run tests
        // -----------------------------------------------------------------------

        /// <summary>
        /// Verifies that Run logs a warning and does not proceed when the singleton lock cannot be acquired.
        /// </summary>
        [Fact]
        public async Task Run_WhenLockNotAcquired_LogsWarningAndDoesNotProceed()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            env.Set("ReconcileSyncBatchSize", "100");
            env.Set("MAX_CORPIDS_ALLOWED", "5000");

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory, acquireLock: false);
            TimerInfo timer = MakeTimerInfo(withScheduleStatus: false);

            await sut.Run(timer);

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Warning && l.Message.Contains("Another instance of ReconcileSyncState is already running"));

            // GetEnvironmentVariables should NOT have been called (no batch-size warning)
            Assert.DoesNotContain(
                loggerFactory.Logger.Logs,
                l => l.Message.Contains("C# Timer trigger function executed at"));
        }

        /// <summary>
        /// Verifies that Run does not log the next schedule when the timer has no schedule status.
        /// </summary>
        [Fact]
        public async Task Run_WhenScheduleStatusIsNull_DoesNotLogNextSchedule()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "false");
            env.Set("ReconcileSyncBatchSize", "100");
            env.Set("MAX_CORPIDS_ALLOWED", "5000");

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory);
            TimerInfo timer = MakeTimerInfo(withScheduleStatus: false);

            await sut.Run(timer);

            Assert.DoesNotContain(
                loggerFactory.Logger.Logs,
                l => l.Message.Contains("Next timer schedule at"));
        }

        /// <summary>
        /// Verifies that Run logs the next scheduled time when the timer has a schedule status.
        /// </summary>
        [Fact]
        public async Task Run_WhenScheduleStatusIsNotNull_LogsNextSchedule()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "false");
            env.Set("ReconcileSyncBatchSize", "100");
            env.Set("MAX_CORPIDS_ALLOWED", "5000");

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory);
            TimerInfo timer = MakeTimerInfo(withScheduleStatus: true);

            await sut.Run(timer);

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Information && l.Message.Contains("Next timer schedule at"));
        }

        /// <summary>
        /// Verifies that Run logs an informational message and does not call the database when sync is not enabled.
        /// </summary>
        [Fact]
        public async Task Run_WhenSyncNotEnabled_LogsInfoAndDoesNotCallDb()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "false");
            env.Set("ReconcileSyncBatchSize", "100");
            env.Set("MAX_CORPIDS_ALLOWED", "5000");

            var db = new StubCosmosDbService();
            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory, db);
            TimerInfo timer = MakeTimerInfo(withScheduleStatus: false);

            await sut.Run(timer);

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Information && l.Message.Contains("Syncing not enabled. No work to do."));

            // The completion message should NOT appear since we returned early
            Assert.DoesNotContain(
                loggerFactory.Logger.Logs,
                l => l.Message.Contains("ReconcileSyncState completed"));
        }

        /// <summary>
        /// Verifies that Run completes a full execution including both the non-syncing and syncing device sections when sync is enabled.
        /// </summary>
        [Fact]
        public async Task Run_WhenSyncEnabled_CompletesFullRunWithBothSections()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            env.Set("ReconcileSyncBatchSize", "100");
            env.Set("MAX_CORPIDS_ALLOWED", "5000");

            var db = new StubCosmosDbService
            {
                NonSyncingTagsToReturn = [],
                SyncedDevicesInTagsToReturn = [],
                SyncingTagsToReturn = [],
            };

            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory, db);
            TimerInfo timer = MakeTimerInfo(withScheduleStatus: false);

            await sut.Run(timer);

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Information && l.Message.Contains("Removed Corp IDs for"));

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Information && l.Message.Contains("Added Corp IDs for"));

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Information && l.Message.Contains("ReconcileSyncState completed"));
        }

        /// <summary>
        /// Verifies that Run logs the execution start message when sync is enabled.
        /// </summary>
        [Fact]
        public async Task Run_WhenSyncEnabled_LogsExecutionStart()
        {
            using var env = new EnvVarScope();
            env.Set("EnableCorpIDSync", "true");
            env.Set("ReconcileSyncBatchSize", "100");
            env.Set("MAX_CORPIDS_ALLOWED", "5000");

            var db = new StubCosmosDbService();
            var loggerFactory = new CapturingLoggerFactory();
            ReconcileSyncState sut = CreateSut(loggerFactory, db);
            TimerInfo timer = MakeTimerInfo(withScheduleStatus: false);

            await sut.Run(timer);

            Assert.Contains(
                loggerFactory.Logger.Logs,
                l => l.Level == LogLevel.Information && l.Message.Contains("C# Timer trigger function executed at"));
        }
    }
}
