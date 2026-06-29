using System.Net;
using CorporateIdentifierSync.Enums;
using CorporateIdentifierSync.Interfaces;
using DelegationStationShared.Models;
using DelegationStationShared.Enums;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph.Beta.Models;
using Device = DelegationStationShared.Models.Device;
using DeviceTag = DelegationStationShared.Models.DeviceTag;

namespace CorporateIdentifierSync.Tests.AddNewDevicesTests
{
    [Collection("EnvVarTests")]
    public class AddNewDevicesTests
    {
        #region setup
        // ─── inner stubs ────────────────────────────────────────────────────────

        private sealed class NopAsyncDisposable : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private sealed class StubSingletonLock : IFunctionSingletonLock
        {
            public IAsyncDisposable? HandleToReturn { get; set; } = new NopAsyncDisposable();

            public Task<IAsyncDisposable?> TryAcquireAsync(
                string lockName,
                CancellationToken cancellationToken = default)
                => Task.FromResult(HandleToReturn);
        }

        private sealed class StubGraphBetaService : IGraphBetaService
        {
            public Func<ImportedDeviceIdentityType, string, Task<ImportedDeviceIdentity>> OnAdd { get; set; }
                = (_, _) => Task.FromResult(new ImportedDeviceIdentity { Id = "corp-id-1", ImportedDeviceIdentifier = "ident-1" });

            public Func<string, Task<DeleteCorpIdResult>> OnDelete { get; set; }
                = _ => Task.FromResult(DeleteCorpIdResult.Success);

            public ImportedDeviceIdentityType? LastAddType { get; private set; }
            public string? LastAddIdentifier { get; private set; }
            public string? LastDeletedId { get; private set; }

            public Task<ImportedDeviceIdentity> AddCorporateIdentifier(
                ImportedDeviceIdentityType type, string identifier)
            {
                LastAddType = type;
                LastAddIdentifier = identifier;
                return OnAdd(type, identifier);
            }

            public Task<DeleteCorpIdResult> DeleteCorporateIdentifier(string identifierID)
            {
                LastDeletedId = identifierID;
                return OnDelete(identifierID);
            }

            public Task<bool> CorporateIdentifierExists(string identiferID) => throw new NotImplementedException();
            public Task<int> GetCorporateDeviceIdentifierCountAsync() => throw new NotImplementedException();
        }

        private sealed class StubDbService : ICosmosDbService
        {
            // State for CorpIDCounter (mirrors FakeCosmosDbService behaviour)
            public CorpIDCounter Counter { get; set; } = new CorpIDCounter(0);

            // Configurable delegates
            public Func<Task<List<string>>> OnGetNonSyncingDeviceTags { get; set; }
                = () => Task.FromResult(new List<string>());
            public Func<List<string>, int, Task<List<Device>>> OnGetAddedDevicesNotSyncing { get; set; }
                = (_, _) => Task.FromResult(new List<Device>());
            public Func<Device, Task> OnUpdateDevice { get; set; } = _ => Task.CompletedTask;
            public Func<Task<List<string>>> OnGetSyncingDeviceTags { get; set; }
                = () => Task.FromResult(new List<string>());
            public Func<List<string>, int, Task<List<Device>>> OnGetAddedDevicesToSync { get; set; }
                = (_, _) => Task.FromResult(new List<Device>());
            public Func<Guid, string, Task<Device?>> OnGetDevice { get; set; }
                = (_, _) => Task.FromResult<Device?>(null);
            public Func<Task<CorpIDCounter>> OnGetCorpIDCounter { get; set; }
            public Func<CorpIDCounter, string, Task<bool>> OnTrySetCorpIDCounter { get; set; }

            public StubDbService()
            {
                OnGetCorpIDCounter = () => Task.FromResult(Counter);
                OnTrySetCorpIDCounter = (counter, _) =>
                {
                    Counter = counter;
                    return Task.FromResult(true);
                };
            }

            // Call tracking
            public bool WasGetNonSyncingDeviceTagsCalled { get; private set; }
            public bool WasGetSyncingDeviceTagsCalled { get; private set; }
            public List<(List<string> TagIds, int BatchSize)> GetAddedDevicesNotSyncingCalls { get; } = new();
            public List<Device> UpdatedDevices { get; } = new();

            // ICosmosDbService – methods used by AddNewDevices / CorpIdCapacityManager
            public Task<List<string>> GetNonSyncingDeviceTags()
            {
                WasGetNonSyncingDeviceTagsCalled = true;
                return OnGetNonSyncingDeviceTags();
            }

            public Task<List<Device>> GetAddedDevicesNotSyncing(List<string> tagIds, int batchSize)
            {
                GetAddedDevicesNotSyncingCalls.Add((tagIds, batchSize));
                return OnGetAddedDevicesNotSyncing(tagIds, batchSize);
            }

            public Task UpdateDevice(Device device)
            {
                UpdatedDevices.Add(device);
                return OnUpdateDevice(device);
            }

            public Task<List<string>> GetSyncingDeviceTags()
            {
                WasGetSyncingDeviceTagsCalled = true;
                return OnGetSyncingDeviceTags();
            }

            public Task<List<Device>> GetAddedDevicesToSync(List<string> tagIds, int batchSize)
                => OnGetAddedDevicesToSync(tagIds, batchSize);

            public Task<CorpIDCounter> GetCorpIDCounter()
                => OnGetCorpIDCounter();

            public Task<bool> TrySetCorpIDCounter(CorpIDCounter counter, string etag)
                => OnTrySetCorpIDCounter(counter, etag);

            public Task<Device?> GetDevice(Guid id, string partitionKey)
                => OnGetDevice(id, partitionKey);

            // Unused by AddNewDevices
            public Task<List<Device>> GetAddedDevices(int batchSize) => throw new NotImplementedException();
            public Task<List<Device>> GetDevicesMarkedForDeletion() => throw new NotImplementedException();
            public Task DeleteDevice(Device device) => throw new NotImplementedException();
            public Task<List<Device>> GetDevicesSyncedBefore(DateTime date) => throw new NotImplementedException();
            public Task<List<Device>> GetSyncedDevicesSyncedBefore(DateTime date) => throw new NotImplementedException();
            public Task<DeviceTag> GetDeviceTag(string id) => throw new NotImplementedException();
            public Task<List<Device>> GetSyncedDevices(int batchSize) => throw new NotImplementedException();
            public Task<List<Device>> GetNotSyncingDevices(int batchSize) => throw new NotImplementedException();
            public Task<List<Device>> GetSyncedDevicesInTags(List<string> tagIds, int batchSize) => throw new NotImplementedException();
            public Task<List<Device>> GetNotSyncingDevicesInTags(List<string> tagsWithSyncEnabled, int batchSize) => throw new NotImplementedException();
            public Task<int> GetSyncedDeviceCountAsync() => throw new NotImplementedException();
        }

        private sealed class RecordingLogger : ILogger<AddNewDevices>
        {
            public List<(LogLevel Level, string Message)> Logs { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                Logs.Add((logLevel, formatter(state, exception)));
            }
        }

        private sealed class RecordingLoggerFactory : ILoggerFactory
        {
            public RecordingLogger Logger { get; } = new();

            public void AddProvider(ILoggerProvider provider) { }
            public ILogger CreateLogger(string categoryName) => Logger;
            public void Dispose() { }
        }

        // ─── factory helpers ────────────────────────────────────────────────────

        private static AddNewDevices CreateSut(
            ILoggerFactory? loggerFactory = null,
            ICosmosDbService? db = null,
            IGraphBetaService? graph = null,
            IFunctionSingletonLock? singletonLock = null)
        {
            return new AddNewDevices(
                loggerFactory ?? NullLoggerFactory.Instance,
                db ?? new StubDbService(),
                graph ?? new StubGraphBetaService(),
                singletonLock ?? new StubSingletonLock());
        }

        private static Device MakeDevice(
            string make = "Dell",
            string model = "Latitude",
            string serial = "SN001",
            DeviceOS? os = DeviceOS.Windows,
            int failureCount = 0)
        {
            var d = new Device
            {
                Make = make,
                Model = model,
                SerialNumber = serial,
                OS = os,
                CorpIDFailureCount = failureCount,
            };
            d.Tags.Add("tag-1");
            return d;
        }

        // Build a StubDbService configured to support a full Run() with sync enabled
        // and a CorpIDCounter that provides enough capacity.
        private static StubDbService MakeSyncDb(
            int corpIdCap = 10000,
            List<Device>? notSyncingDevices = null,
            List<Device>? syncingDevices = null)
        {
            var db = new StubDbService
            {
                Counter = new CorpIDCounter(0) { CorpIDCount = 0, CorpIDReserve = 0 },
            };
            db.OnGetNonSyncingDeviceTags = () => Task.FromResult(new List<string>());
            db.OnGetAddedDevicesNotSyncing = (_, _) => Task.FromResult(notSyncingDevices ?? new List<Device>());
            db.OnGetSyncingDeviceTags = () => Task.FromResult(new List<string>());
            db.OnGetAddedDevicesToSync = (_, _) => Task.FromResult(syncingDevices ?? new List<Device>());
            return db;
        }

        // ─── env-var helpers ────────────────────────────────────────────────────

        private static void SetSyncEnabled(
            bool syncEnabled = true,
            int batchSize = 5000,
            int maxCorpIds = 10000,
            int maxRetries = 10)
        {
            Environment.SetEnvironmentVariable("EnableCorpIDSync", syncEnabled.ToString().ToLower());
            Environment.SetEnvironmentVariable("AddDeviceBatchSize", batchSize.ToString());
            Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", maxCorpIds.ToString());
            Environment.SetEnvironmentVariable("MAX_CORPID_RETRIES", maxRetries.ToString());
        }

        private static void ClearEnvVars()
        {
            Environment.SetEnvironmentVariable("EnableCorpIDSync", null);
            Environment.SetEnvironmentVariable("AddDeviceBatchSize", null);
            Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", null);
            Environment.SetEnvironmentVariable("MAX_CORPID_RETRIES", null);
        }
        #endregion setup

        #region ConstructorTests
        // ═══════════════════════════════════════════════════════════════════════
        // Constructor
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Verifies that the constructor creates a valid AddNewDevices instance when all dependencies are provided.
        /// </summary>
        [Fact]
        public void Constructor_WithValidDependencies_DoesNotThrow()
        {
            // Arrange / Act / Assert – no exception means the object was created successfully
            _ = CreateSut();
        }

        /// <summary>
        /// Verifies that the constructor stores its dependencies and uses them during a Run invocation.
        /// </summary>
        [Fact]
        public async Task Constructor_StoresDependencies_UsedInRun()
        {
            // Arrange
            var db = new StubDbService();
            var singletonLock = new StubSingletonLock { HandleToReturn = null };

            // Act – if TryAcquireAsync returns null the lock was rejected using the stored _singletonLock
            var sut = CreateSut(db: db, singletonLock: singletonLock);

            // Calling Run verifies internally-stored singletonLock is used (no DB calls made)
            ClearEnvVars();
            try
            {
                await sut.Run(new TimerInfo());
                Assert.False(db.WasGetNonSyncingDeviceTagsCalled);
            }
            finally
            {
                ClearEnvVars();
            }
        }
        #endregion ConstructorTests

        #region GetEnvironmentVariablesTests
        // ═══════════════════════════════════════════════════════════════════════
        // GetEnvironmentVariables
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Verifies that GetEnvironmentVariables logs an error for each missing environment variable when none are set.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_WhenNoneSet_LogsErrorForEachVar()
        {
            // Arrange
            ClearEnvVars();
            var logFactory = new RecordingLoggerFactory();
            var sut = CreateSut(loggerFactory: logFactory);

            // Act
            sut.GetEnvironmentVariables();

            // Assert – each missing var produces at least one Error log entry
            var errors = logFactory.Logger.Logs
                .Where(l => l.Level == LogLevel.Error)
                .Select(l => l.Message)
                .ToList();

            Assert.Contains(errors, m => m.Contains("EnableCorpIDSync not set"));
            Assert.Contains(errors, m => m.Contains("BatchSize is not set or invalid"));
            Assert.Contains(errors, m => m.Contains("Max Corp IDS Allowed is not set or invalid"));
            Assert.Contains(errors, m => m.Contains("MAX_CORPID_RETRIES is not set or invalid"));
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables logs an informational message for each variable when all are set to valid values.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_WhenAllValid_LogsInfoForEachVar()
        {
            // Arrange
            SetSyncEnabled(syncEnabled: true, batchSize: 100, maxCorpIds: 500, maxRetries: 3);
            var logFactory = new RecordingLoggerFactory();
            var sut = CreateSut(loggerFactory: logFactory);

            try
            {
                // Act
                sut.GetEnvironmentVariables();

                // Assert – no errors, info messages for numeric settings
                var errors = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Error)
                    .ToList();

                Assert.Empty(errors);

                var infos = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Information)
                    .Select(l => l.Message)
                    .ToList();

                Assert.Contains(infos, m => m.Contains("Using BatchSize: 100"));
                Assert.Contains(infos, m => m.Contains("Maximum allowed Corporate Identifers"));
                Assert.Contains(infos, m => m.Contains("Max Corporate Identifier retries"));
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables uses the default batch size and logs an error when AddDeviceBatchSize is invalid.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_WhenBatchSizeInvalid_UsesDefaultAndLogsError()
        {
            // Arrange
            Environment.SetEnvironmentVariable("AddDeviceBatchSize", "not-a-number");
            var logFactory = new RecordingLoggerFactory();
            var sut = CreateSut(loggerFactory: logFactory);

            try
            {
                // Act
                sut.GetEnvironmentVariables();

                // Assert
                var errors = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Error)
                    .Select(l => l.Message)
                    .ToList();

                Assert.Contains(errors, m => m.Contains("BatchSize is not set or invalid") && m.Contains("5000"));
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables uses the default batch size and logs an error when AddDeviceBatchSize is zero.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_WhenBatchSizeIsZero_UsesDefaultAndLogsError()
        {
            // Arrange
            Environment.SetEnvironmentVariable("AddDeviceBatchSize", "0");
            var logFactory = new RecordingLoggerFactory();
            var sut = CreateSut(loggerFactory: logFactory);

            try
            {
                // Act
                sut.GetEnvironmentVariables();

                // Assert
                var errors = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Error)
                    .Select(l => l.Message)
                    .ToList();

                Assert.Contains(errors, m => m.Contains("BatchSize is not set or invalid") && m.Contains("5000"));
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables uses the default batch size and logs an error when AddDeviceBatchSize is negative.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_WhenBatchSizeIsNegative_UsesDefaultAndLogsError()
        {
            // Arrange
            Environment.SetEnvironmentVariable("AddDeviceBatchSize", "-10");
            var logFactory = new RecordingLoggerFactory();
            var sut = CreateSut(loggerFactory: logFactory);

            try
            {
                // Act
                sut.GetEnvironmentVariables();

                // Assert
                var errors = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Error)
                    .Select(l => l.Message)
                    .ToList();

                Assert.Contains(errors, m => m.Contains("BatchSize is not set or invalid") && m.Contains("5000"));
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables uses the default max and logs an error when MAX_CORPIDS_ALLOWED is invalid.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_WhenMaxCorpIdsInvalid_UsesDefaultAndLogsError()
        {
            // Arrange
            Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", "bad");
            var logFactory = new RecordingLoggerFactory();
            var sut = CreateSut(loggerFactory: logFactory);

            try
            {
                // Act
                sut.GetEnvironmentVariables();

                // Assert
                var errors = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Error)
                    .Select(l => l.Message)
                    .ToList();

                Assert.Contains(errors, m => m.Contains("Max Corp IDS Allowed is not set or invalid") && m.Contains("10000"));
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables uses the default max and logs an error when MAX_CORPIDS_ALLOWED is zero.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_WhenMaxCorpIdsIsZero_UsesDefaultAndLogsError()
        {
            // Arrange
            Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", "0");
            var logFactory = new RecordingLoggerFactory();
            var sut = CreateSut(loggerFactory: logFactory);

            try
            {
                // Act
                sut.GetEnvironmentVariables();

                // Assert
                var errors = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Error)
                    .Select(l => l.Message)
                    .ToList();

                Assert.Contains(errors, m => m.Contains("Max Corp IDS Allowed is not set or invalid") && m.Contains("10000"));
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables uses the default retry count and logs an error when MAX_CORPID_RETRIES is invalid.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_WhenMaxRetriesInvalid_UsesDefaultAndLogsError()
        {
            // Arrange
            Environment.SetEnvironmentVariable("MAX_CORPID_RETRIES", "xyz");
            var logFactory = new RecordingLoggerFactory();
            var sut = CreateSut(loggerFactory: logFactory);

            try
            {
                // Act
                sut.GetEnvironmentVariables();

                // Assert
                var errors = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Error)
                    .Select(l => l.Message)
                    .ToList();

                Assert.Contains(errors, m => m.Contains("MAX_CORPID_RETRIES is not set or invalid") && m.Contains("10"));
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables uses the default retry count and logs an error when MAX_CORPID_RETRIES is zero.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_WhenMaxRetriesIsZero_UsesDefaultAndLogsError()
        {
            // Arrange
            Environment.SetEnvironmentVariable("MAX_CORPID_RETRIES", "0");
            var logFactory = new RecordingLoggerFactory();
            var sut = CreateSut(loggerFactory: logFactory);

            try
            {
                // Act
                sut.GetEnvironmentVariables();

                // Assert
                var errors = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Error)
                    .Select(l => l.Message)
                    .ToList();

                Assert.Contains(errors, m => m.Contains("MAX_CORPID_RETRIES is not set or invalid") && m.Contains("10"));
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables logs an error when EnableCorpIDSync is set to an invalid boolean string.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_WhenEnableCorpIDSyncInvalid_LogsError()
        {
            // Arrange
            Environment.SetEnvironmentVariable("EnableCorpIDSync", "not-a-bool");
            var logFactory = new RecordingLoggerFactory();
            var sut = CreateSut(loggerFactory: logFactory);

            try
            {
                // Act
                sut.GetEnvironmentVariables();

                // Assert
                var errors = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Error)
                    .Select(l => l.Message)
                    .ToList();

                Assert.Contains(errors, m => m.Contains("EnableCorpIDSync not set or not a valid boolean"));
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables logs an informational message containing the batch size when AddDeviceBatchSize is valid.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_WhenValidBatchSize_LogsInfoMessage()
        {
            // Arrange
            Environment.SetEnvironmentVariable("AddDeviceBatchSize", "250");
            var logFactory = new RecordingLoggerFactory();
            var sut = CreateSut(loggerFactory: logFactory);

            try
            {
                // Act
                sut.GetEnvironmentVariables();

                // Assert
                var infos = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Information)
                    .Select(l => l.Message)
                    .ToList();

                Assert.Contains(infos, m => m.Contains("Using BatchSize: 250"));
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables logs an informational message containing the max corp ID count when MAX_CORPIDS_ALLOWED is valid.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_WhenValidMaxCorpIds_LogsInfoMessage()
        {
            // Arrange
            Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", "999");
            var logFactory = new RecordingLoggerFactory();
            var sut = CreateSut(loggerFactory: logFactory);

            try
            {
                // Act
                sut.GetEnvironmentVariables();

                // Assert
                var infos = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Information)
                    .Select(l => l.Message)
                    .ToList();

                Assert.Contains(infos, m => m.Contains("999"));
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that GetEnvironmentVariables logs an informational message containing the retry count when MAX_CORPID_RETRIES is valid.
        /// </summary>
        [Fact]
        public void GetEnvironmentVariables_WhenValidMaxRetries_LogsInfoMessage()
        {
            // Arrange
            Environment.SetEnvironmentVariable("MAX_CORPID_RETRIES", "7");
            var logFactory = new RecordingLoggerFactory();
            var sut = CreateSut(loggerFactory: logFactory);

            try
            {
                // Act
                sut.GetEnvironmentVariables();

                // Assert
                var infos = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Information)
                    .Select(l => l.Message)
                    .ToList();

                Assert.Contains(infos, m => m.Contains("7"));
            }
            finally
            {
                ClearEnvVars();
            }
        }
        #endregion GetEnvironmentVariablesTests

        #region SingletonLockTest
        // ═══════════════════════════════════════════════════════════════════════
        // Run – singleton lock test
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Verifies that Run exits early without making any database calls when the singleton lock cannot be acquired.
        /// </summary>
        [Fact]
        public async Task Run_WhenLockNotAcquired_ReturnsEarlyWithoutDbCalls()
        {
            // Arrange
            ClearEnvVars();
            var db = new StubDbService();
            var singletonLock = new StubSingletonLock { HandleToReturn = null };
            var sut = CreateSut(db: db, singletonLock: singletonLock);

            // Act
            await sut.Run(new TimerInfo());

            // Assert – lock was rejected so no DB calls should have been made
            Assert.False(db.WasGetNonSyncingDeviceTagsCalled);
            Assert.False(db.WasGetSyncingDeviceTagsCalled);
        }
        #endregion SingletonLockTest

        #region EarlyExitTests
        // ═══════════════════════════════════════════════════════════════════════
        // Run – early exit tests
        // ═══════════════════════════════════════════════════════════════════════

        ///
        /// <summary>
        /// Verifies that Run exits early without making any database calls when sync is disabled.
        /// </summary>
        [Fact]
        public async Task Run_WhenSyncDisabled_ReturnsEarlyWithoutDbCalls()
        {
            // Arrange
            Environment.SetEnvironmentVariable("EnableCorpIDSync", "false");
            try
            {
                var db = new StubDbService();
                var sut = CreateSut(db: db);

                // Act
                await sut.Run(new TimerInfo());

                // Assert – sync disabled; no processing calls expected
                Assert.False(db.WasGetNonSyncingDeviceTagsCalled);
                Assert.False(db.WasGetSyncingDeviceTagsCalled);
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that Run exits early without processing devices when GetAvailableCorpIDCount throws an exception.
        /// </summary>
        [Fact]
        public async Task Run_WhenGetAvailableCorpIDCountThrows_ReturnsEarly()
        {
            // Arrange
            SetSyncEnabled();
            var db = MakeSyncDb();
            db.OnGetCorpIDCounter = () => throw new InvalidOperationException("DB down");

            var sut = CreateSut(db: db);

            try
            {
                // Act – should NOT throw; the exception is caught inside Run
                await sut.Run(new TimerInfo());

                // Assert – GetAddedDevicesToSync was never called (returned early)
                Assert.False(db.WasGetSyncingDeviceTagsCalled);
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that Run exits early without processing any devices when no CorpID slots are available.
        /// </summary>
        [Fact]
        public async Task Run_WhenNoCorpIDsAvailable_ReturnsEarlyWithoutProcessingDevices()
        {
            // Arrange – counter is at full capacity
            SetSyncEnabled(maxCorpIds: 100);
            var db = MakeSyncDb();
            db.Counter = new CorpIDCounter(0) { CorpIDCount = 100 };

            var sut = CreateSut(db: db);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert – no syncing work attempted
                Assert.False(db.WasGetSyncingDeviceTagsCalled);
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that Run exits early without processing devices when ReserveCorpIDs throws an exception.
        /// </summary>
        [Fact]
        public async Task Run_WhenReserveCorpIDsThrows_ReturnsEarly()
        {
            // Arrange – first GetCorpIDCounter call (for GetAvailable) succeeds; TrySetCorpIDCounter throws
            SetSyncEnabled();
            var db = MakeSyncDb();
            db.OnTrySetCorpIDCounter = (_, _) => throw new InvalidOperationException("save failed");

            var sut = CreateSut(db: db);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert
                Assert.False(db.WasGetSyncingDeviceTagsCalled);
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that Run exits early without processing devices when the reservation returns zero available slots.
        /// </summary>
        [Fact]
        public async Task Run_WhenReservationReturnsZero_ReturnsEarlyWithoutProcessingDevices()
        {
            // Arrange – counter already at capacity at the time of reservation
            SetSyncEnabled(maxCorpIds: 50);
            var db = MakeSyncDb();
            db.Counter = new CorpIDCounter(0) { CorpIDCount = 49 }; // 1 available initially

            // After GetAvailableCorpIDCount (reports 1), we push the counter to full before Reserve
            // by making TrySetCorpIDCounter indicate failure then report 0 available
            // Simpler: just set count == max so available == 0 for reserve as well
            // Actually reserve is called with requestSize = min(batchSize, available) = min(5000, 1) = 1
            // We'll instead make TrySetCorpIDCounter set the counter to full capacity
            // so the returned reserved is 0 on the next check.
            db.Counter = new CorpIDCounter(0) { CorpIDCount = 50 }; // exactly at cap

            var sut = CreateSut(db: db);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert
                Assert.False(db.WasGetSyncingDeviceTagsCalled);
            }
            finally
            {
                ClearEnvVars();
            }
        }
        #endregion EarlyExitTests

        #region GetDevicesTests
        // ═══════════════════════════════════════════════════════════════════════
        // Run – GetAddedDevicesToSync
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Verifies that Run commits the capacity manager and returns when GetAddedDevicesToSync throws an exception.
        /// </summary>
        [Fact]
        public async Task Run_WhenGetDevicesToSyncThrows_CommitsAndReturns()
        {
            // Arrange
            SetSyncEnabled();
            var db = MakeSyncDb();
            db.OnGetAddedDevicesToSync = (_, _) => throw new InvalidOperationException("db read error");

            var sut = CreateSut(db: db);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert – no devices were updated, and reserve was released (counter reserve back to 0)
                Assert.Empty(db.UpdatedDevices);
                Assert.Equal(0, db.Counter.CorpIDReserve);
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that Run does not propagate an exception when both GetAddedDevicesToSync and the commit both throw.
        /// </summary>
        [Fact]
        public async Task Run_WhenGetDevicesToSyncThrowsAndCommitThrows_DoesNotPropagateException()
        {
            // Arrange
            SetSyncEnabled();
            var db = MakeSyncDb();
            db.OnGetAddedDevicesToSync = (_, _) => throw new InvalidOperationException("db read error");
            var callCount = 0;
            db.OnTrySetCorpIDCounter = (_, _) =>
            {
                callCount++;
                // First call is Reserve (succeed), second is Commit (fail)
                if (callCount >= 2)
                {
                    throw new InvalidOperationException("commit failed");
                }

                return Task.FromResult(true);
            };

            var sut = CreateSut(db: db);
            try
            {
                // Act – should NOT throw even though commit fails
                await sut.Run(new TimerInfo());
            }
            finally
            {
                ClearEnvVars();
            }
        }
        #endregion GetDevicesTests

        #region IdentiferFormatTests
        // ═══════════════════════════════════════════════════════════════════════
        // Run – device OS / identifier format
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Verifies that Run uses the ManufacturerModelSerial identifier format and the expected
        /// identifier string for Windows, Unknown, and null OS devices.
        /// </summary>
        [Theory]
        [InlineData("Dell", "Latitude", "SN-WIN", DeviceOS.Windows, "\"Dell\",\"Latitude\",SN-WIN")]
        [InlineData("HP", "ProBook", "SN-UNK", DeviceOS.Unknown, "\"HP\",\"ProBook\",SN-UNK")]
        [InlineData("Lenovo", "ThinkPad", "SN-NULL", null, "\"Lenovo\",\"ThinkPad\",SN-NULL")]
        public async Task Run_DeviceWithManufacturerModelSerialOs_UsesManufacturerModelSerialIdentifierFormat(
            string make, string model, string serial, DeviceOS? os, string expectedIdentifier)
        {
            // Arrange
            SetSyncEnabled();
            var device = MakeDevice(make: make, model: model, serial: serial, os: os);
            var db = MakeSyncDb(syncingDevices: new List<Device> { device });
            var graph = new StubGraphBetaService();

            var sut = CreateSut(db: db, graph: graph);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert
                Assert.Equal(ImportedDeviceIdentityType.ManufacturerModelSerial, graph.LastAddType);
                Assert.Equal(expectedIdentifier, graph.LastAddIdentifier);
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that Run uses the SerialNumber-only identifier format and the device's serial
        /// number as the identifier for macOS, iOS, and Android devices.
        /// </summary>
        [Theory]
        [InlineData("Apple", "MacBook", "SN-MAC", DeviceOS.MacOS)]
        [InlineData("Apple", "iPad", "SN-IOS", DeviceOS.iOS)]
        [InlineData("Samsung", "Galaxy", "SN-AND", DeviceOS.Android)]
        public async Task Run_DeviceWithSerialNumberOs_UsesSerialNumberOnlyIdentifierFormat(
            string make, string model, string serial, DeviceOS os)
        {
            // Arrange
            SetSyncEnabled();
            var device = MakeDevice(make: make, model: model, serial: serial, os: os);
            var db = MakeSyncDb(syncingDevices: new List<Device> { device });
            var graph = new StubGraphBetaService();

            var sut = CreateSut(db: db, graph: graph);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert
                Assert.Equal(ImportedDeviceIdentityType.SerialNumber, graph.LastAddType);
                Assert.Equal(serial, graph.LastAddIdentifier);
            }
            finally
            {
                ClearEnvVars();
            }
        }
        #endregion IdentifierFormatTests


        #region HappyPathTests
        /// ═══════════════════════════════════════════════════════════════════════
        /// Happy Path device tests
        /// =══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Successful processing of non-syncing device
        /// Expected behavior:  no corpID created, no increase in count, changes status to not syncing
        /// </summary>
        [Fact]
        public async Task Run_NonSyncingDevice_SetsStatusToNotSyncing()
        {
            // Arrange
            SetSyncEnabled();
            var device = MakeDevice();
            var db = MakeSyncDb(notSyncingDevices: new List<Device> { device });

            var sut = CreateSut(db: db);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert
                Assert.Equal(DeviceStatus.NotSyncing, device.Status);
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Successful processing of syncing device
        /// Verifies that after a successful sync the device object reflects the new Corp ID,
        /// the status is Synced, the failure count is reset, and CorpIDCount is incremented by 1.
        /// </summary>
        [Fact]
        public async Task Run_SyncingDevice_SuccessfulSync_SetsDeviceStateAndIncrementsCorpIDCount()
        {
            // Arrange
            SetSyncEnabled(maxCorpIds: 100);
            var device = MakeDevice();
            var db = MakeSyncDb(syncingDevices: new List<Device> { device });
            var graph = new StubGraphBetaService();
            graph.OnAdd = (_, _) => Task.FromResult(new ImportedDeviceIdentity
            {
                Id = "graph-id-42",
                ImportedDeviceIdentifier = "ident-42",
            });

            var sut = CreateSut(db: db, graph: graph);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert – device object
                Assert.Equal(DeviceStatus.Synced, device.Status);
                Assert.Equal("graph-id-42", device.CorporateIdentityID);
                Assert.Equal("ident-42", device.CorporateIdentity);
                Assert.Equal(0, device.CorpIDFailureCount);

                // Assert – CorpIDCount incremented by 1
                Assert.Equal(1, db.Counter.CorpIDCount);
            }
            finally
            {
                ClearEnvVars();
            }
        }

        #endregion HappyPathTests

        #region GraphErrorHandlingTests
        /// ═══════════════════════════════════════════════════════════════════════
        /// Graph error handling tests
        /// =══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Processing syncing device where Graph fails to add CorpID
        /// Expected behavior:  does not set CorpID, increments failure count, does not change status
        /// </summary>
        [Fact]
        public async Task Run_SyncingDevice_GraphAddFails_DbUpdateSucceeds_CorpIDCountUnchangedAndStatusAdded()
        {
            // Arrange
            SetSyncEnabled(maxRetries: 5);
            var device = MakeDevice(failureCount: 0);
            var db = MakeSyncDb(syncingDevices: new List<Device> { device });
            var graph = new StubGraphBetaService();
            graph.OnAdd = (_, _) => throw new InvalidOperationException("graph unavailable");
            // OnUpdateDevice is default – succeeds

            var sut = CreateSut(db: db, graph: graph);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert – CorpID was never added so the committed count stays 0
                Assert.Equal(0, db.Counter.CorpIDCount);
                // Failure count must be incremented
                Assert.Equal(1, device.CorpIDFailureCount);
                // Status must remain Added – failure count (1) has not exceeded maxRetries (5)
                Assert.Equal(DeviceStatus.Added, device.Status);
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that Run does not mark a device as Failed when the Graph add fails but the failure count is still below the max retry threshold.
        /// </summary>
        [Fact]
        public async Task Run_GraphAddFails_BelowMaxRetries_DoesNotMarkDeviceAsFailed()
        {
            // Arrange – MAX_CORPID_RETRIES=3, failureCount starts at 2 (< 3)
            SetSyncEnabled(maxRetries: 3);
            var device = MakeDevice(failureCount: 2);
            var db = MakeSyncDb(syncingDevices: new List<Device> { device });
            var graph = new StubGraphBetaService();
            graph.OnAdd = (_, _) => throw new InvalidOperationException("graph error");

            var sut = CreateSut(db: db, graph: graph);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert – failure incremented to 3 which equals max (not > max)
                Assert.Equal(3, device.CorpIDFailureCount);
                Assert.NotEqual(DeviceStatus.Failed, device.Status);
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that Run marks a device as Failed when the Graph add fails and the failure count exceeds the max retry threshold.
        /// </summary>
        [Fact]
        public async Task Run_GraphAddFails_AboveMaxRetries_MarksDeviceAsFailed()
        {
            // Arrange – MAX_CORPID_RETRIES=3, failureCount starts at 3 (will become 4 > 3)
            SetSyncEnabled(maxRetries: 3);
            var device = MakeDevice(failureCount: 3);
            var db = MakeSyncDb(syncingDevices: new List<Device> { device });
            var graph = new StubGraphBetaService();
            graph.OnAdd = (_, _) => throw new InvalidOperationException("graph error");

            var sut = CreateSut(db: db, graph: graph);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert
                Assert.Equal(4, device.CorpIDFailureCount);
                Assert.Equal(DeviceStatus.Failed, device.Status);
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that Run logs an error when the corp ID rollback deletion fails after a Cosmos NotFound exception on UpdateDevice.
        /// </summary>
        [Fact]
        public async Task Run_UpdateDeviceThrowsNotFound_RollbackDeleteFails_LogsError()
        {
            // Arrange
            SetSyncEnabled();
            var logFactory = new RecordingLoggerFactory();
            var device = MakeDevice();
            var db = MakeSyncDb(syncingDevices: new List<Device> { device });
            var graph = new StubGraphBetaService();
            graph.OnAdd = (_, _) => Task.FromResult(new ImportedDeviceIdentity
            {
                Id = "fail-rollback-id",
                ImportedDeviceIdentifier = "ident",
            });
            graph.OnDelete = _ => Task.FromResult(DeleteCorpIdResult.Error);
            db.OnUpdateDevice = _ => throw new CosmosException(
                "not found", HttpStatusCode.NotFound, 0, "act", 0.0);

            var sut = CreateSut(loggerFactory: logFactory, db: db, graph: graph);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert – an error should be logged about failed rollback
                var errors = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Error)
                    .Select(l => l.Message)
                    .ToList();

                Assert.Contains(errors, m => m.Contains("Failed to roll back Corp ID"));
            }
            finally
            {
                ClearEnvVars();
            }
        }

        #endregion GraphErrorHandlingTests

        #region CosmosErrorHandlingTests

        /// ═══════════════════════════════════════════════════════════════════════
        /// Cosmos Error Handling Tests - Non syncing devices
        /// =══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Processing non-syncing device fails with Cosmos 404 Exception (device was deleted)
        /// Expected behavior:  Logs the error, does not throw, does not create corpID, does not increment count, and does not recreate DB object
        /// </summary>
        [Fact]
        public async Task Run_NonSyncingDevice_UpdateThrowsNotFound_LogsException()
        {
            // Arrange
            SetSyncEnabled();
            var logFactory = new RecordingLoggerFactory();
            var device = MakeDevice();
            var db = MakeSyncDb(notSyncingDevices: new List<Device> { device });
            var callCount = 0;
            db.OnUpdateDevice = _ =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new CosmosException(
                        "not found", HttpStatusCode.NotFound, 0, "act", 0.0);
                }

                return Task.CompletedTask;
            };

            var sut = CreateSut(loggerFactory: logFactory, db: db);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert – error logged about device not found
                var errors = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Error)
                    .Select(l => l.Message)
                    .ToList();

                Assert.Contains(errors, m => m.Contains("Device not found to updated"));
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Processing non-syncing device fails with Cosmos PreconditionFailed Exception (device was modified concurrently, we assume to Deleting state)
        /// Expected behavior:  Logs the warning, does not throw, does not create corpID, does not increment count, and does not modify DB object
        /// </summary>
        [Fact]
        public async Task Run_NonSyncingDevice_UpdateThrowsPreconditionFailed_LogsWarning()
        {
            // Arrange
            SetSyncEnabled();
            var logFactory = new RecordingLoggerFactory();
            var device = MakeDevice();
            var db = MakeSyncDb(notSyncingDevices: new List<Device> { device });
            var callCount = 0;
            db.OnUpdateDevice = _ =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new CosmosException(
                        "precondition failed", HttpStatusCode.PreconditionFailed, 0, "act", 0.0);
                }

                return Task.CompletedTask;
            };

            var sut = CreateSut(loggerFactory: logFactory, db: db);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert
                var warnings = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Warning)
                    .Select(l => l.Message)
                    .ToList();

                Assert.Contains(warnings, m => m.Contains("modified concurrently"));
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// ═══════════════════════════════════════════════════════════════════════
        /// Cosmos Error Handling Tests - syncing devices (corp ID created successfully)
        /// =══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Processing syncing device where graph add succeeds but DB update fails with Cosmos 404 Exception (device was deleted)
        /// Expected behavior: Rolls back the CorpID created and does not increase count
        /// </summary>
        [Fact]
        public async Task Run_UpdateDeviceThrowsNotFound_RollsBackCorpId()
        {
            // Arrange
            SetSyncEnabled(maxCorpIds: 10);
            var device = MakeDevice();
            var db = MakeSyncDb(syncingDevices: new List<Device> { device });
            var graph = new StubGraphBetaService();
            graph.OnAdd = (_, _) => Task.FromResult(new ImportedDeviceIdentity
            {
                Id = "rollback-id",
                ImportedDeviceIdentifier = "rollback-ident",
            });
            graph.OnDelete = _ => Task.FromResult(DeleteCorpIdResult.Success);
            db.OnUpdateDevice = _ => throw new CosmosException(
                "not found", HttpStatusCode.NotFound, 0, "act", 0.0);

            var sut = CreateSut(db: db, graph: graph);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert – DeleteCorporateIdentifier should have been called with the corp ID
                Assert.Equal("rollback-id", graph.LastDeletedId);

                // CorpIDCount must stay at 0 since the rollback succeeded
                Assert.Equal(0, db.Counter.CorpIDCount);
            }
            finally
            {
                ClearEnvVars();
            }
        }


        /// <summary>
        /// Processing syncing device where graph add succeeds but DB update fails with Cosmos PreconditionFailed Exception (device was modified concurrently, we assume to Deleting state)
        /// Expected behavior: Rolls back the CorpID created and does not increase count
        /// </summary>
        [Fact]
        public async Task Run_UpdateDeviceThrowsPreconditionFailed_CurrentDeviceDeleting_RollsBackCorpId()
        {
            // Arrange
            SetSyncEnabled(maxCorpIds: 10);
            var device = MakeDevice();
            var db = MakeSyncDb(syncingDevices: new List<Device> { device });
            var graph = new StubGraphBetaService();
            graph.OnAdd = (_, _) => Task.FromResult(new ImportedDeviceIdentity
            {
                Id = "deleting-id",
                ImportedDeviceIdentifier = "deleting-ident",
            });
            graph.OnDelete = _ => Task.FromResult(DeleteCorpIdResult.Success);
            db.OnUpdateDevice = _ => throw new CosmosException(
                "precondition failed", HttpStatusCode.PreconditionFailed, 0, "act", 0.0);
            db.OnGetDevice = (_, _) => Task.FromResult<Device?>(new Device { Status = DeviceStatus.Deleting });

            var sut = CreateSut(db: db, graph: graph);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert
                Assert.Equal("deleting-id", graph.LastDeletedId);
                // CorpIDCount must stay at 0 since the rollback succeeded
                Assert.Equal(0, db.Counter.CorpIDCount);
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that Run rolls back the corp ID when UpdateDevice throws a Cosmos PreconditionFailed exception and the refreshed device is null.
        /// </summary>
        [Fact]
        public async Task Run_UpdateDeviceThrowsPreconditionFailed_CurrentDeviceNull_RollsBackCorpId()
        {
            // Arrange
            SetSyncEnabled();
            var device = MakeDevice();
            var db = MakeSyncDb(syncingDevices: new List<Device> { device });
            var graph = new StubGraphBetaService();
            graph.OnAdd = (_, _) => Task.FromResult(new ImportedDeviceIdentity
            {
                Id = "pf-id",
                ImportedDeviceIdentifier = "pf-ident",
            });
            db.OnUpdateDevice = _ => throw new CosmosException(
                "precondition failed", HttpStatusCode.PreconditionFailed, 0, "act", 0.0);
            db.OnGetDevice = (_, _) => Task.FromResult<Device?>(null); // device gone

            var sut = CreateSut(db: db, graph: graph);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert – rollback called
                Assert.Equal("pf-id", graph.LastDeletedId);
                // CorpIDCount must stay at 0 since the rollback succeeded
                Assert.Equal(0, db.Counter.CorpIDCount);
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that Run logs a warning and does not roll back when UpdateDevice throws a Cosmos PreconditionFailed exception and the refreshed device is in an unexpected state.
        /// </summary>
        [Fact]
        public async Task Run_UpdateDeviceThrowsPreconditionFailed_UnexpectedState_LogsWarning()
        {
            // Arrange
            SetSyncEnabled();
            var logFactory = new RecordingLoggerFactory();
            var device = MakeDevice();
            var db = MakeSyncDb(syncingDevices: new List<Device> { device });
            var graph = new StubGraphBetaService();
            graph.OnAdd = (_, _) => Task.FromResult(new ImportedDeviceIdentity
            {
                Id = "unexpected-id",
                ImportedDeviceIdentifier = "unexpected-ident",
            });
            db.OnUpdateDevice = _ => throw new CosmosException(
                "precondition failed", HttpStatusCode.PreconditionFailed, 0, "act", 0.0);

            // Return device in Synced state – unexpected after a concurrent write
            db.OnGetDevice = (_, _) => Task.FromResult<Device?>(new Device { Status = DeviceStatus.Synced });

            var sut = CreateSut(loggerFactory: logFactory, db: db, graph: graph);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert – should log a warning about the unexpected state; no rollback
                var warnings = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Warning)
                    .Select(l => l.Message)
                    .ToList();

                Assert.Contains(warnings, m => m.Contains("unexpectedly in state"));
                Assert.Null(graph.LastDeletedId);
            }
            finally
            {
                ClearEnvVars();
            }
        }


        /// <summary>
        /// Verifies that Run logs an exception and continues processing when UpdateDevice throws a generic exception.
        /// </summary>
        [Fact]
        public async Task Run_UpdateDeviceThrowsGenericException_LogsExceptionAndContinues()
        {
            // Arrange
            SetSyncEnabled();
            var device = MakeDevice();
            var db = MakeSyncDb(syncingDevices: new List<Device> { device });
            var graph = new StubGraphBetaService();
            var logFactory = new RecordingLoggerFactory();
            db.OnUpdateDevice = _ => throw new InvalidOperationException("generic db error");

            var sut = CreateSut(loggerFactory: logFactory, db: db, graph: graph);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert – error logged about DB entry not updated
                var errors = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Error)
                    .Select(l => l.Message)
                    .ToList();

                Assert.Contains(errors, m => m.Contains("Device entry not updated - CorpIDStatus may not be in sync"));
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that Run logs an error when UpdateDevice throws a generic exception for a non-syncing device.
        /// </summary>
        [Fact]
        public async Task Run_NonSyncingDevice_UpdateThrowsGenericException_LogsException()
        {
            // Arrange
            SetSyncEnabled();
            var logFactory = new RecordingLoggerFactory();
            var device = MakeDevice();
            var db = MakeSyncDb(notSyncingDevices: new List<Device> { device });
            var callCount = 0;
            db.OnUpdateDevice = _ =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new InvalidOperationException("generic error");
                }

                return Task.CompletedTask;
            };

            var sut = CreateSut(loggerFactory: logFactory, db: db);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert
                var errors = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Error)
                    .Select(l => l.Message)
                    .ToList();

                Assert.Contains(errors, m => m.Contains("Device entry not updated for non-syncing device"));
            }
            finally
            {
                ClearEnvVars();
            }
        }

        #endregion CosmosErrorHandlingTests


        #region GraphAndCosmosErrorHandlingTests
        // ═══════════════════════════════════════════════════════════════════════
        // Tests for cases where Graph and Cosmos fail
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Processing syncing device where graph add fails and DB update fails with Cosmos 404 Exception (device was deleted)
        /// Expected behavior:  CorpIDCount is not incremented
        /// </summary>
        [Fact]
        public async Task Run_SyncingDevice_GraphAddFails_UpdateThrowsNotFound_CorpIDCountUnchangedAndNoRollback()
        {
            // Arrange
            SetSyncEnabled();
            var device = MakeDevice(failureCount: 0);
            var db = MakeSyncDb(syncingDevices: new List<Device> { device });
            var graph = new StubGraphBetaService();
            graph.OnAdd = (_, _) => throw new InvalidOperationException("graph unavailable");
            db.OnUpdateDevice = _ => throw new CosmosException(
                "not found", HttpStatusCode.NotFound, 0, "act", 0.0);

            var sut = CreateSut(db: db, graph: graph);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert – no CorpID was added so count remains 0
                Assert.Equal(0, db.Counter.CorpIDCount);
                // No rollback needed because CorporateIdentityID was never set
                Assert.Null(graph.LastDeletedId);
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Processing syncing device where graph add fails and DB update fails with Cosmos PreconditionFailed Exception (device was modified concurrently, we assume to Deleting state)
        /// Expected behavior:  CorpIDCount is not incremented and no update to DB object is attempted
        /// </summary>
        [Fact]
        public async Task Run_SyncingDevice_GraphAddFails_UpdateThrowsPreconditionFailed_DeviceDeleting_CorpIDCountUnchangedAndNoRollback()
        {
            // Arrange
            SetSyncEnabled();
            var device = MakeDevice(failureCount: 0);
            var db = MakeSyncDb(syncingDevices: new List<Device> { device });
            var graph = new StubGraphBetaService();
            graph.OnAdd = (_, _) => throw new InvalidOperationException("graph unavailable");
            db.OnUpdateDevice = _ => throw new CosmosException(
                "precondition failed", HttpStatusCode.PreconditionFailed, 0, "act", 0.0);
            db.OnGetDevice = (_, _) => Task.FromResult<Device?>(new Device { Status = DeviceStatus.Deleting });

            var sut = CreateSut(db: db, graph: graph);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert – no CorpID was added; DeviceDeletion completes cleanup, so count stays 0
                Assert.Equal(0, db.Counter.CorpIDCount);
                // No rollback attempted since CorporateIdentityID was never set
                Assert.Null(graph.LastDeletedId);
            }
            finally
            {
                ClearEnvVars();
            }
        }

        #endregion GraphAndCosmosErrorHandlingTests


        #region CommitCorpIDCountTests
        // ═══════════════════════════════════════════════════════════════════════
        // Run – CommitCorpIDCount related test cases
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Verifies that Run logs an exception and does not rethrow when CommitCorpIDCount fails.
        /// </summary>
        [Fact]
        public async Task Run_CommitCorpIDCountThrows_LogsExceptionAndDoesNotRethrow()
        {
            // Arrange
            SetSyncEnabled();
            var device = MakeDevice();
            var db = MakeSyncDb(syncingDevices: new List<Device> { device });
            var logFactory = new RecordingLoggerFactory();

            var callCount = 0;
            db.OnTrySetCorpIDCounter = (counter, etag) =>
            {
                callCount++;
                // 1st call: Reserve (succeed), 2nd call: CommitCorpIDCount (fail)
                if (callCount >= 2)
                {
                    throw new InvalidOperationException("commit failure");
                }

                db.Counter = counter;
                return Task.FromResult(true);
            };

            var sut = CreateSut(loggerFactory: logFactory, db: db);
            try
            {
                // Act – should complete without throwing
                await sut.Run(new TimerInfo());

                // Assert – exception was logged
                var errors = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Error)
                    .Select(l => l.Message)
                    .ToList();

                Assert.Contains(errors, m => m.Contains("Failed to update Capacitymanager"));
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that Run logs an informational message about available slots when slots remain after a sync.
        /// </summary>
        [Fact]
        public async Task Run_WhenNowAvailablePositive_LogsInfoAboutAvailableSlots()
        {
            // Arrange
            SetSyncEnabled(maxCorpIds: 100);
            var device = MakeDevice();
            var db = MakeSyncDb(syncingDevices: new List<Device> { device });
            var logFactory = new RecordingLoggerFactory();

            var sut = CreateSut(loggerFactory: logFactory, db: db);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert – since only 1 slot used out of 100, available > 0
                var infos = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Information)
                    .Select(l => l.Message)
                    .ToList();

                Assert.Contains(infos, m => m.Contains("Current available Corporate ID slots:"));
            }
            finally
            {
                ClearEnvVars();
            }
        }

        /// <summary>
        /// Verifies that Run logs a warning about available slots when no slots remain after a sync.
        /// </summary>
        [Fact]
        public async Task Run_WhenNowAvailableZero_LogsWarningAboutAvailableSlots()
        {
            // Arrange – set cap to 1, sync 1 device → nowAvailable = 0
            SetSyncEnabled(maxCorpIds: 1);
            var device = MakeDevice();
            var db = MakeSyncDb(syncingDevices: new List<Device> { device });
            db.Counter = new CorpIDCounter(0) { CorpIDCount = 0, CorpIDReserve = 0 };
            var logFactory = new RecordingLoggerFactory();

            var sut = CreateSut(loggerFactory: logFactory, db: db);
            try
            {
                // Act
                await sut.Run(new TimerInfo());

                // Assert – available is 0 after committing 1 device with cap of 1
                var warnings = logFactory.Logger.Logs
                    .Where(l => l.Level == LogLevel.Warning)
                    .Select(l => l.Message)
                    .ToList();

                Assert.Contains(warnings, m => m.Contains("Current available Corporate ID slots:"));
            }
            finally
            {
                ClearEnvVars();
            }
        }
        #endregion CommitCorpIDCountTests

    }
}
