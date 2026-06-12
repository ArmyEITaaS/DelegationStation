using System.Net;
using CorporateIdentifierSync.Enums;
using CorporateIdentifierSync.Interfaces;
using CorporateIdentifierSync.Models;
using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph.Beta.Models;
using Device = DelegationStationShared.Models.Device;
using DeviceTag = DelegationStationShared.Models.DeviceTag;

namespace CorporateIdentifierSync.Tests.ConfirmSyncTests;

[Collection("EnvVarTests")]
public class ConfirmSyncTests
{
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
        public Func<string, Task<bool>> OnExists { get; set; } = _ => Task.FromResult(true);
        public Func<ImportedDeviceIdentityType, string, Task<ImportedDeviceIdentity>> OnAdd { get; set; }
            = (_, _) => Task.FromResult(new ImportedDeviceIdentity { Id = "corp-id-1", ImportedDeviceIdentifier = "ident-1" });
        public Func<string, Task<DeleteCorpIdResult>> OnDelete { get; set; }
            = _ => Task.FromResult(DeleteCorpIdResult.Success);

        public ImportedDeviceIdentityType? LastAddType { get; private set; }
        public string? LastAddIdentifier { get; private set; }
        public string? LastDeletedId { get; private set; }
        public int ExistsCallCount { get; private set; }

        public Task<bool> CorporateIdentifierExists(string identiferID)
        {
            ExistsCallCount++;
            return OnExists(identiferID);
        }

        public Task<ImportedDeviceIdentity> AddCorporateIdentifier(ImportedDeviceIdentityType type, string identifier)
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

        public Task<int> GetCorporateDeviceIdentifierCountAsync() => throw new NotImplementedException();
    }

    private sealed class StubDbService : ICosmosDbService
    {
        public CorpIDCounter Counter { get; set; } = new CorpIDCounter(0);

        public Func<Task<List<string>>> OnGetSyncingDeviceTags { get; set; }
            = () => Task.FromResult(new List<string> { "tag-1" });
        public Func<DateTime, Task<List<Device>>> OnGetSyncedDevicesSyncedBefore { get; set; }
            = _ => Task.FromResult(new List<Device>());
        public Func<Device, Task> OnUpdateDevice { get; set; } = _ => Task.CompletedTask;
        public Func<Guid, string, Task<Device?>> OnGetDevice { get; set; }
            = (_, _) => Task.FromResult<Device?>(null);
        public Func<Task<CorpIDCounter>> OnGetCorpIDCounter { get; set; }
        public Func<CorpIDCounter, string, Task<bool>> OnTrySetCorpIDCounter { get; set; }

        public int GetCorpIDCounterCallCount { get; private set; }
        public List<Device> UpdatedDevices { get; } = new();
        public bool WasGetSyncingDeviceTagsCalled { get; private set; }

        public StubDbService()
        {
            OnGetCorpIDCounter = () =>
            {
                GetCorpIDCounterCallCount++;
                return Task.FromResult(Counter);
            };
            OnTrySetCorpIDCounter = (counter, _) =>
            {
                Counter = counter;
                return Task.FromResult(true);
            };
        }

        public Task<List<string>> GetSyncingDeviceTags()
        {
            WasGetSyncingDeviceTagsCalled = true;
            return OnGetSyncingDeviceTags();
        }

        public Task<List<Device>> GetSyncedDevicesSyncedBefore(DateTime date)
            => OnGetSyncedDevicesSyncedBefore(date);

        public Task UpdateDevice(Device device)
        {
            UpdatedDevices.Add(device);
            return OnUpdateDevice(device);
        }

        public Task<Device?> GetDevice(Guid id, string partitionKey)
            => OnGetDevice(id, partitionKey);

        public Task<CorpIDCounter> GetCorpIDCounter()
            => OnGetCorpIDCounter();

        public Task<bool> TrySetCorpIDCounter(CorpIDCounter counter, string etag)
            => OnTrySetCorpIDCounter(counter, etag);

        // Unused by ConfirmSync
        public Task<List<Device>> GetAddedDevices(int batchSize) => throw new NotImplementedException();
        public Task<List<Device>> GetAddedDevicesNotSyncing(List<string> tagIds, int batchSize) => throw new NotImplementedException();
        public Task<List<Device>> GetAddedDevicesToSync(List<string> tagIds, int batchSize) => throw new NotImplementedException();
        public Task<List<Device>> GetDevicesMarkedForDeletion() => throw new NotImplementedException();
        public Task DeleteDevice(Device device) => throw new NotImplementedException();
        public Task<List<Device>> GetDevicesSyncedBefore(DateTime date) => throw new NotImplementedException();
        public Task<DeviceTag> GetDeviceTag(string id) => throw new NotImplementedException();
        public Task<List<string>> GetNonSyncingDeviceTags() => throw new NotImplementedException();
        public Task<List<Device>> GetSyncedDevicesInTags(List<string> tagIds, int batchSize) => throw new NotImplementedException();
        public Task<List<Device>> GetNotSyncingDevicesInTags(List<string> tagsWithSyncEnabled, int batchSize) => throw new NotImplementedException();
    }

    private sealed class RecordingLogger : ILogger<ConfirmSync>
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

    private static ConfirmSync CreateSut(
        ILoggerFactory? loggerFactory = null,
        ICosmosDbService? db = null,
        IGraphBetaService? graph = null,
        IFunctionSingletonLock? singletonLock = null)
    {
        return new ConfirmSync(
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
        string corpId = "",
        string tagId = "tag-1",
        DeviceStatus status = DeviceStatus.Synced)
    {
        var d = new Device
        {
            Make = make,
            Model = model,
            SerialNumber = serial,
            OS = os,
            CorporateIdentityID = corpId,
            Status = status,
        };
        d.Tags.Add(tagId);
        return d;
    }

    private static StubDbService MakeSyncDb(
        List<Device>? candidates = null,
        List<string>? syncEnabledTags = null)
    {
        var db = new StubDbService();
        db.OnGetSyncingDeviceTags = () => Task.FromResult(syncEnabledTags ?? new List<string> { "tag-1" });
        db.OnGetSyncedDevicesSyncedBefore = _ => Task.FromResult(candidates ?? new List<Device>());
        return db;
    }

    private static void SetSyncEnabled(
        bool syncEnabled = true,
        int syncIntervalHours = 24,
        int maxCorpIds = 10000)
    {
        Environment.SetEnvironmentVariable("EnableCorpIDSync", syncEnabled.ToString().ToLower());
        Environment.SetEnvironmentVariable("SyncIntervalHours", syncIntervalHours.ToString());
        Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", maxCorpIds.ToString());
    }

    private static void ClearEnvVars()
    {
        Environment.SetEnvironmentVariable("EnableCorpIDSync", null);
        Environment.SetEnvironmentVariable("SyncIntervalHours", null);
        Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", null);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Constructor
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that the <see cref="ConfirmSync"/> constructor does not throw
    /// when all dependencies are provided as valid stubs.
    /// </summary>
    [Fact]
    public void Constructor_WithValidDependencies_DoesNotThrow()
    {
        // Arrange / Act / Assert
        _ = CreateSut();
    }

    /// <summary>
    /// Verifies that when the singleton lock cannot be acquired (returns <see langword="null"/>),
    /// <see cref="ConfirmSync.Run"/> exits early without making any database calls.
    /// </summary>
    [Fact]
    public async Task Constructor_StoresSingletonLock_WhenLockReturnsNull_ExitsEarlyWithoutDbCalls()
    {
        // Arrange
        ClearEnvVars();
        var db = new StubDbService();
        var singletonLock = new StubSingletonLock { HandleToReturn = null };
        var sut = CreateSut(db: db, singletonLock: singletonLock);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert – no DB calls made because lock rejected
            Assert.False(db.WasGetSyncingDeviceTagsCalled);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that the injected <see cref="ICosmosDbService"/> is stored and used during
    /// <see cref="ConfirmSync.Run"/> by confirming that <c>GetSyncingDeviceTags</c> is called
    /// on the injected instance.
    /// </summary>
    [Fact]
    public async Task Constructor_StoresDbService_UsedDuringRun()
    {
        // Arrange
        SetSyncEnabled();
        var db = MakeSyncDb(syncEnabledTags: new List<string> { "tag-1" });

        var sut = CreateSut(db: db);
        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert – GetSyncingDeviceTags was called on the injected db
            Assert.True(db.WasGetSyncingDeviceTagsCalled);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetEnvironmentVariables
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that <see cref="ConfirmSync.GetEnvironmentVariables"/> logs an error when
    /// the <c>EnableCorpIDSync</c> environment variable is not set.
    /// </summary>
    [Fact]
    public void GetEnvironmentVariables_WhenEnableCorpIDSyncNotSet_LogsError()
    {
        // Arrange
        ClearEnvVars();
        var logFactory = new RecordingLoggerFactory();
        var sut = CreateSut(loggerFactory: logFactory);

        // Act
        sut.GetEnvironmentVariables();

        // Assert
        var errors = logFactory.Logger.Logs
            .Where(l => l.Level == LogLevel.Error)
            .Select(l => l.Message)
            .ToList();

        Assert.Contains(errors, m => m.Contains("EnableCorpIDSync not set or not a valid boolean"));
    }

    /// <summary>
    /// Verifies that <see cref="ConfirmSync.GetEnvironmentVariables"/> logs an error when
    /// the <c>EnableCorpIDSync</c> environment variable is set to a non-boolean string.
    /// </summary>
    [Fact]
    public void GetEnvironmentVariables_WhenEnableCorpIDSyncInvalidString_LogsError()
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
    /// Verifies that <see cref="ConfirmSync.GetEnvironmentVariables"/> does not log an error
    /// for <c>EnableCorpIDSync</c> when the variable is set to <c>true</c>.
    /// </summary>
    [Fact]
    public void GetEnvironmentVariables_WhenEnableCorpIDSyncIsTrue_NoSyncFlagError()
    {
        // Arrange
        Environment.SetEnvironmentVariable("EnableCorpIDSync", "true");
        var logFactory = new RecordingLoggerFactory();
        var sut = CreateSut(loggerFactory: logFactory);

        try
        {
            // Act
            sut.GetEnvironmentVariables();

            // Assert – no error for EnableCorpIDSync
            var errors = logFactory.Logger.Logs
                .Where(l => l.Level == LogLevel.Error)
                .Select(l => l.Message)
                .ToList();

            Assert.DoesNotContain(errors, m => m.Contains("EnableCorpIDSync not set or not a valid boolean"));
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that <see cref="ConfirmSync.GetEnvironmentVariables"/> logs an error when
    /// the <c>SyncIntervalHours</c> environment variable is not set.
    /// </summary>
    [Fact]
    public void GetEnvironmentVariables_WhenSyncIntervalHoursNotSet_LogsError()
    {
        // Arrange
        ClearEnvVars();
        var logFactory = new RecordingLoggerFactory();
        var sut = CreateSut(loggerFactory: logFactory);

        // Act
        sut.GetEnvironmentVariables();

        // Assert
        var errors = logFactory.Logger.Logs
            .Where(l => l.Level == LogLevel.Error)
            .Select(l => l.Message)
            .ToList();

        Assert.Contains(errors, m => m.Contains("SyncIntervalHours is not set or not a valid integer"));
    }

    /// <summary>
    /// Verifies that <see cref="ConfirmSync.GetEnvironmentVariables"/> logs an error when
    /// the <c>SyncIntervalHours</c> environment variable is set to a non-integer string.
    /// </summary>
    [Fact]
    public void GetEnvironmentVariables_WhenSyncIntervalHoursInvalid_LogsError()
    {
        // Arrange
        Environment.SetEnvironmentVariable("SyncIntervalHours", "not-a-number");
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

            Assert.Contains(errors, m => m.Contains("SyncIntervalHours is not set or not a valid integer"));
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that <see cref="ConfirmSync.GetEnvironmentVariables"/> logs an informational
    /// message containing the configured value when <c>SyncIntervalHours</c> is a valid integer.
    /// </summary>
    [Fact]
    public void GetEnvironmentVariables_WhenSyncIntervalHoursValid_LogsInfo()
    {
        // Arrange
        Environment.SetEnvironmentVariable("SyncIntervalHours", "48");
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

            Assert.Contains(infos, m => m.Contains("Using SyncIntervalHours: 48"));
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that <see cref="ConfirmSync.GetEnvironmentVariables"/> falls back to the default
    /// of 10,000 and logs an error when <c>MAX_CORPIDS_ALLOWED</c> is not set.
    /// </summary>
    [Fact]
    public void GetEnvironmentVariables_WhenMaxCorpIDsNotSet_UsesDefaultAndLogsError()
    {
        // Arrange
        ClearEnvVars();
        var logFactory = new RecordingLoggerFactory();
        var sut = CreateSut(loggerFactory: logFactory);

        // Act
        sut.GetEnvironmentVariables();

        // Assert
        var errors = logFactory.Logger.Logs
            .Where(l => l.Level == LogLevel.Error)
            .Select(l => l.Message)
            .ToList();

        Assert.Contains(errors, m => m.Contains("MAX_CORPIDS_ALLOWED is not set or invalid") && m.Contains("10000"));
    }

    /// <summary>
    /// Verifies that <see cref="ConfirmSync.GetEnvironmentVariables"/> falls back to the default
    /// of 10,000 and logs an error when <c>MAX_CORPIDS_ALLOWED</c> is set to an invalid string.
    /// </summary>
    [Fact]
    public void GetEnvironmentVariables_WhenMaxCorpIDsInvalid_UsesDefaultAndLogsError()
    {
        // Arrange
        Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", "bad-value");
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

            Assert.Contains(errors, m => m.Contains("MAX_CORPIDS_ALLOWED is not set or invalid") && m.Contains("10000"));
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that <see cref="ConfirmSync.GetEnvironmentVariables"/> falls back to the default
    /// of 10,000 and logs an error when <c>MAX_CORPIDS_ALLOWED</c> is set to zero.
    /// </summary>
    [Fact]
    public void GetEnvironmentVariables_WhenMaxCorpIDsIsZero_UsesDefaultAndLogsError()
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

            Assert.Contains(errors, m => m.Contains("MAX_CORPIDS_ALLOWED is not set or invalid") && m.Contains("10000"));
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that <see cref="ConfirmSync.GetEnvironmentVariables"/> logs an informational
    /// message containing the configured value when <c>MAX_CORPIDS_ALLOWED</c> is set to a
    /// valid positive integer.
    /// </summary>
    [Fact]
    public void GetEnvironmentVariables_WhenMaxCorpIDsValid_LogsInfo()
    {
        // Arrange
        Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", "5000");
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

            Assert.Contains(infos, m => m.Contains("5000"));
        }
        finally
        {
            ClearEnvVars();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Run – singleton lock
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that <see cref="ConfirmSync.Run"/> exits immediately without making any database
    /// calls when the singleton lock cannot be acquired.
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

        // Assert
        Assert.False(db.WasGetSyncingDeviceTagsCalled);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Run – timer schedule status
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that <see cref="ConfirmSync.Run"/> logs the next scheduled run time when
    /// <see cref="TimerInfo.ScheduleStatus"/> is populated.
    /// </summary>
    [Fact]
    public async Task Run_WithScheduleStatus_LogsNextSchedule()
    {
        // Arrange
        SetSyncEnabled();
        var logFactory = new RecordingLoggerFactory();
        var db = MakeSyncDb();
        var timer = new TimerInfo
        {
            ScheduleStatus = new ScheduleStatus { Next = DateTime.UtcNow.AddHours(24) },
        };
        var sut = CreateSut(loggerFactory: logFactory, db: db);

        try
        {
            // Act
            await sut.Run(timer);

            // Assert
            var infos = logFactory.Logger.Logs
                .Where(l => l.Level == LogLevel.Information)
                .Select(l => l.Message)
                .ToList();

            Assert.Contains(infos, m => m.Contains("Next timer schedule at:"));
        }
        finally
        {
            ClearEnvVars();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Run – sync disabled
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that <see cref="ConfirmSync.Run"/> exits early without querying the database
    /// when <c>EnableCorpIDSync</c> is set to <c>false</c>.
    /// </summary>
    [Fact]
    public async Task Run_WhenSyncDisabled_ReturnsEarlyWithoutDbCalls()
    {
        // Arrange
        SetSyncEnabled(syncEnabled: false);
        var db = MakeSyncDb();
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

    // ═══════════════════════════════════════════════════════════════════════
    // Run – sync interval validation
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that <see cref="ConfirmSync.Run"/> logs an error and exits early without querying
    /// the database when <c>SyncIntervalHours</c> is configured with a negative value.
    /// </summary>
    [Fact]
    public async Task Run_WhenSyncIntervalHoursNegative_LogsErrorAndReturnsEarly()
    {
        // Arrange
        SetSyncEnabled(syncIntervalHours: -1);
        var logFactory = new RecordingLoggerFactory();
        var db = MakeSyncDb();
        var sut = CreateSut(loggerFactory: logFactory, db: db);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert – error logged for negative interval
            var errors = logFactory.Logger.Logs
                .Where(l => l.Level == LogLevel.Error)
                .Select(l => l.Message)
                .ToList();

            Assert.Contains(errors, m => m.Contains("SyncIntervalHours is not set or invalid"));
            Assert.False(db.WasGetSyncingDeviceTagsCalled);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that <see cref="ConfirmSync.Run"/> treats a <c>SyncIntervalHours</c> value of zero
    /// as valid and continues processing by querying sync-enabled device tags.
    /// </summary>
    [Fact]
    public async Task Run_WhenSyncIntervalHoursIsZero_ContinuesProcessing()
    {
        // Arrange
        SetSyncEnabled(syncIntervalHours: 0);
        var db = MakeSyncDb();
        var sut = CreateSut(db: db);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert – zero is valid, continues to GetSyncingDeviceTags
            Assert.True(db.WasGetSyncingDeviceTagsCalled);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Run – no sync-enabled tags
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that <see cref="ConfirmSync.Run"/> exits early without querying any devices
    /// when no device tags with sync enabled are found.
    /// </summary>
    [Fact]
    public async Task Run_WhenNoTagsWithSyncEnabled_ReturnsEarlyWithoutQueryingDevices()
    {
        // Arrange
        SetSyncEnabled();
        var db = new StubDbService();
        db.OnGetSyncingDeviceTags = () => Task.FromResult(new List<string>());
        var getDevicesCalled = false;
        db.OnGetSyncedDevicesSyncedBefore = _ =>
        {
            getDevicesCalled = true;
            return Task.FromResult(new List<Device>());
        };
        var sut = CreateSut(db: db);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert – no devices queried since no tags
            Assert.False(getDevicesCalled);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Run – device filtering
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that devices whose tags do not appear in the sync-enabled tag list are filtered
    /// out and do not trigger a call to <see cref="ICosmosDbService.UpdateDevice"/>.
    /// </summary>
    [Fact]
    public async Task Run_WhenDeviceNotInSyncEnabledTag_IsFiltered()
    {
        // Arrange
        SetSyncEnabled();
        var deviceWithWrongTag = MakeDevice(tagId: "other-tag");
        var db = MakeSyncDb(
            candidates: new List<Device> { deviceWithWrongTag },
            syncEnabledTags: new List<string> { "tag-1" });
        var sut = CreateSut(db: db);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert – device not in sync-enabled tags, so UpdateDevice never called
            Assert.Empty(db.UpdatedDevices);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that devices with no associated tags are filtered out and do not trigger a call
    /// to <see cref="ICosmosDbService.UpdateDevice"/>.
    /// </summary>
    [Fact]
    public async Task Run_WhenDeviceHasNoTags_IsFiltered()
    {
        // Arrange
        SetSyncEnabled();
        var deviceWithNoTags = new Device
        {
            Make = "Dell",
            Model = "Latitude",
            SerialNumber = "SN001",
            OS = DeviceOS.Windows,
            CorporateIdentityID = "corp-id",
        };
        var db = MakeSyncDb(candidates: new List<Device> { deviceWithNoTags });
        var sut = CreateSut(db: db);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert – device has no tags, so filtered out
            Assert.Empty(db.UpdatedDevices);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Run – corpIDFound path
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that when a device's corporate identifier is confirmed to exist in Microsoft Graph,
    /// <c>LastCorpIdentitySync</c> is updated to the current time and
    /// <see cref="ICosmosDbService.UpdateDevice"/> is called.
    /// </summary>
    [Fact]
    public async Task Run_WhenCorpIDExists_UpdatesLastSyncTimeAndCallsUpdateDevice()
    {
        // Arrange
        SetSyncEnabled();
        var device = MakeDevice(corpId: "existing-corp-id");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService { OnExists = _ => Task.FromResult(true) };
        var sut = CreateSut(db: db, graph: graph);

        try
        {
            // Act
            var before = DateTime.UtcNow;
            await sut.Run(new TimerInfo());

            // Assert
            Assert.Single(db.UpdatedDevices);
            Assert.True(device.LastCorpIdentitySync >= before);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that when <see cref="IGraphBetaService.CorporateIdentifierExists"/> throws an
    /// exception, the affected device is skipped and
    /// <see cref="ICosmosDbService.UpdateDevice"/> is not called.
    /// </summary>
    [Fact]
    public async Task Run_WhenCorpIDExistsCheckThrows_SkipsDeviceAndDoesNotCallUpdateDevice()
    {
        // Arrange
        SetSyncEnabled();
        var device = MakeDevice(corpId: "corp-id-1");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService
        {
            OnExists = _ => throw new InvalidOperationException("graph error"),
        };
        var sut = CreateSut(db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert – device skipped; no update
            Assert.Empty(db.UpdatedDevices);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Run – device with no CorporateIdentityID
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that when a device has an empty <c>CorporateIdentityID</c>, a warning is logged
    /// and <see cref="IGraphBetaService.AddCorporateIdentifier"/> is called to re-add the identifier.
    /// </summary>
    [Fact]
    public async Task Run_WhenDeviceHasEmptyCorporateIdentityID_LogsWarningAndAttemptsReAdd()
    {
        // Arrange
        SetSyncEnabled();
        var device = MakeDevice(corpId: ""); // empty → logs warning, tries re-add
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var logFactory = new RecordingLoggerFactory();
        var graph = new StubGraphBetaService();
        var sut = CreateSut(loggerFactory: logFactory, db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert – warning logged, and AddCorporateIdentifier was called
            var warnings = logFactory.Logger.Logs
                .Where(l => l.Level == LogLevel.Warning)
                .Select(l => l.Message)
                .ToList();

            Assert.Contains(warnings, m => m.Contains("Device does not have a CorporateIdentityID stored in DB"));
            Assert.NotNull(graph.LastAddIdentifier);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Run – corpID re-add identifier format
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that a Windows device whose corporate identifier is not found in Graph is
    /// re-added using <see cref="ImportedDeviceIdentityType.ManufacturerModelSerial"/> format
    /// (<c>"Make","Model",Serial</c>).
    /// </summary>
    [Fact]
    public async Task Run_WhenCorpIDNotFound_WindowsDevice_UsesManufacturerModelSerialFormat()
    {
        // Arrange
        SetSyncEnabled();
        var device = MakeDevice(make: "Dell", model: "Latitude", serial: "SN-WIN", os: DeviceOS.Windows, corpId: "corp-id");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService
        {
            OnExists = _ => Task.FromResult(false),
        };
        var sut = CreateSut(db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert
            Assert.Equal(ImportedDeviceIdentityType.ManufacturerModelSerial, graph.LastAddType);
            Assert.Equal("\"Dell\",\"Latitude\",SN-WIN", graph.LastAddIdentifier);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that a device with an unknown OS whose corporate identifier is not found in Graph
    /// is re-added using <see cref="ImportedDeviceIdentityType.ManufacturerModelSerial"/> format.
    /// </summary>
    [Fact]
    public async Task Run_WhenCorpIDNotFound_UnknownOsDevice_UsesManufacturerModelSerialFormat()
    {
        // Arrange
        SetSyncEnabled();
        var device = MakeDevice(make: "HP", model: "ProBook", serial: "SN-UNK", os: DeviceOS.Unknown, corpId: "corp-id");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService { OnExists = _ => Task.FromResult(false) };
        var sut = CreateSut(db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert
            Assert.Equal(ImportedDeviceIdentityType.ManufacturerModelSerial, graph.LastAddType);
            Assert.Equal("\"HP\",\"ProBook\",SN-UNK", graph.LastAddIdentifier);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that a macOS device whose corporate identifier is not found in Graph is re-added
    /// using <see cref="ImportedDeviceIdentityType.SerialNumber"/> format (serial number only).
    /// </summary>
    [Fact]
    public async Task Run_WhenCorpIDNotFound_MacOsDevice_UsesSerialNumberOnlyFormat()
    {
        // Arrange
        SetSyncEnabled();
        var device = MakeDevice(make: "Apple", model: "MacBook", serial: "SN-MAC", os: DeviceOS.MacOS, corpId: "corp-id");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService { OnExists = _ => Task.FromResult(false) };
        var sut = CreateSut(db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert
            Assert.Equal(ImportedDeviceIdentityType.SerialNumber, graph.LastAddType);
            Assert.Equal("SN-MAC", graph.LastAddIdentifier);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that an iOS device whose corporate identifier is not found in Graph is re-added
    /// using <see cref="ImportedDeviceIdentityType.SerialNumber"/> format (serial number only).
    /// </summary>
    [Fact]
    public async Task Run_WhenCorpIDNotFound_iOsDevice_UsesSerialNumberOnlyFormat()
    {
        // Arrange
        SetSyncEnabled();
        var device = MakeDevice(make: "Apple", model: "iPhone", serial: "SN-IOS", os: DeviceOS.iOS, corpId: "corp-id");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService { OnExists = _ => Task.FromResult(false) };
        var sut = CreateSut(db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert
            Assert.Equal(ImportedDeviceIdentityType.SerialNumber, graph.LastAddType);
            Assert.Equal("SN-IOS", graph.LastAddIdentifier);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that an Android device whose corporate identifier is not found in Graph is
    /// re-added using <see cref="ImportedDeviceIdentityType.SerialNumber"/> format (serial number only).
    /// </summary>
    [Fact]
    public async Task Run_WhenCorpIDNotFound_AndroidDevice_UsesSerialNumberOnlyFormat()
    {
        // Arrange
        SetSyncEnabled();
        var device = MakeDevice(make: "Samsung", model: "Galaxy", serial: "SN-AND", os: DeviceOS.Android, corpId: "corp-id");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService { OnExists = _ => Task.FromResult(false) };
        var sut = CreateSut(db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert
            Assert.Equal(ImportedDeviceIdentityType.SerialNumber, graph.LastAddType);
            Assert.Equal("SN-AND", graph.LastAddIdentifier);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Run – re-add success/failure
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that when a corporate identifier re-add succeeds, the device's
    /// <c>CorporateIdentityID</c>, <c>CorporateIdentity</c>, <c>Status</c>, and
    /// <c>CorpIDFailureCount</c> are all updated to reflect the successful sync.
    /// </summary>
    [Fact]
    public async Task Run_WhenReAddSucceeds_UpdatesDeviceFieldsCorrectly()
    {
        // Arrange
        SetSyncEnabled();
        var device = MakeDevice(corpId: "");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService
        {
            OnAdd = (_, _) => Task.FromResult(new ImportedDeviceIdentity
            {
                Id = "new-corp-id",
                ImportedDeviceIdentifier = "new-ident",
            }),
        };
        var sut = CreateSut(db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert
            Assert.Equal("new-corp-id", device.CorporateIdentityID);
            Assert.Equal("new-ident", device.CorporateIdentity);
            Assert.Equal(DeviceStatus.Synced, device.Status);
            Assert.Equal(0, device.CorpIDFailureCount);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that when a corporate identifier re-add fails, the device status is reset to
    /// <see cref="DeviceStatus.Added"/> and <c>CorpIDFailureCount</c> is incremented.
    /// </summary>
    [Fact]
    public async Task Run_WhenReAddFails_ResetsDeviceStatusToAdded()
    {
        // Arrange
        SetSyncEnabled();
        var device = MakeDevice(corpId: "");
        device.CorpIDFailureCount = 0;
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService
        {
            OnAdd = (_, _) => throw new InvalidOperationException("graph down"),
        };
        var sut = CreateSut(db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert
            Assert.Equal(string.Empty, device.CorporateIdentityID);
            Assert.Equal(DeviceStatus.Added, device.Status);
            Assert.Equal(1, device.CorpIDFailureCount);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Run – UpdateDevice NotFound exception
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that when <see cref="ICosmosDbService.UpdateDevice"/> throws a
    /// <see cref="CosmosException"/> with <see cref="HttpStatusCode.NotFound"/> in the
    /// corp-ID-found path, no Graph rollback is performed.
    /// </summary>
    [Fact]
    public async Task Run_UpdateDevice_ThrowsNotFound_CorpIDFound_NoRollback()
    {
        // Arrange
        SetSyncEnabled();
        var device = MakeDevice(corpId: "existing-corp-id");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService { OnExists = _ => Task.FromResult(true) };
        db.OnUpdateDevice = _ => throw new CosmosException("not found", HttpStatusCode.NotFound, 0, "act", 0.0);
        var sut = CreateSut(db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert – no rollback for corpIDFound case
            Assert.Null(graph.LastDeletedId);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that when <see cref="ICosmosDbService.UpdateDevice"/> throws a
    /// <see cref="CosmosException"/> with <see cref="HttpStatusCode.NotFound"/> after a successful
    /// corp ID re-add, the newly created corp ID is rolled back via
    /// <see cref="IGraphBetaService.DeleteCorporateIdentifier"/>.
    /// </summary>
    [Fact]
    public async Task Run_UpdateDevice_ThrowsNotFound_CorpIDReAdded_TriggersRollback()
    {
        // Arrange
        SetSyncEnabled();
        var device = MakeDevice(corpId: ""); // triggers re-add path
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService
        {
            OnAdd = (_, _) => Task.FromResult(new ImportedDeviceIdentity { Id = "readded-id", ImportedDeviceIdentifier = "ident" }),
        };
        db.OnUpdateDevice = _ => throw new CosmosException("not found", HttpStatusCode.NotFound, 0, "act", 0.0);
        var sut = CreateSut(db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert – rollback called
            Assert.Equal("readded-id", graph.LastDeletedId);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that when both the corp ID re-add and the subsequent
    /// <see cref="ICosmosDbService.UpdateDevice"/> fail (NotFound), no Graph rollback is attempted
    /// because there is no newly created corp ID to roll back.
    /// </summary>
    [Fact]
    public async Task Run_UpdateDevice_ThrowsNotFound_CorpIDReAddFailed_NoRollback()
    {
        // Arrange
        SetSyncEnabled();
        var device = MakeDevice(corpId: "");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService
        {
            OnAdd = (_, _) => throw new InvalidOperationException("add failed"),
        };
        db.OnUpdateDevice = _ => throw new CosmosException("not found", HttpStatusCode.NotFound, 0, "act", 0.0);
        var sut = CreateSut(db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert – no rollback since add failed (nothing to roll back)
            Assert.Null(graph.LastDeletedId);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Run – UpdateDevice PreconditionFailed exception
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that when <see cref="ICosmosDbService.UpdateDevice"/> throws a
    /// <see cref="CosmosException"/> with <see cref="HttpStatusCode.PreconditionFailed"/> in the
    /// corp-ID-found path, a warning indicating concurrent modification is logged.
    /// </summary>
    [Fact]
    public async Task Run_UpdateDevice_ThrowsPreconditionFailed_CorpIDFound_LogsWarning()
    {
        // Arrange
        SetSyncEnabled();
        var logFactory = new RecordingLoggerFactory();
        var device = MakeDevice(corpId: "existing-id");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService { OnExists = _ => Task.FromResult(true) };
        db.OnUpdateDevice = _ => throw new CosmosException("precondition failed", HttpStatusCode.PreconditionFailed, 0, "act", 0.0);
        var sut = CreateSut(loggerFactory: logFactory, db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert
            var warnings = logFactory.Logger.Logs
                .Where(l => l.Level == LogLevel.Warning)
                .Select(l => l.Message)
                .ToList();

            Assert.Contains(warnings, m => m.Contains("modified concurrently") && m.Contains("Corp ID already confirmed"));
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that when <see cref="ICosmosDbService.UpdateDevice"/> throws
    /// <see cref="HttpStatusCode.PreconditionFailed"/> after a re-add and the re-fetched device
    /// is <see langword="null"/> (deleted concurrently), the newly created corp ID is rolled back.
    /// </summary>
    [Fact]
    public async Task Run_UpdateDevice_ThrowsPreconditionFailed_CorpIDReAdded_FreshDeviceNull_RollsBack()
    {
        // Arrange
        SetSyncEnabled();
        var device = MakeDevice(corpId: "");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService
        {
            OnAdd = (_, _) => Task.FromResult(new ImportedDeviceIdentity { Id = "pf-corp-id", ImportedDeviceIdentifier = "ident" }),
        };
        db.OnUpdateDevice = _ => throw new CosmosException("precondition failed", HttpStatusCode.PreconditionFailed, 0, "act", 0.0);
        db.OnGetDevice = (_, _) => Task.FromResult<Device?>(null); // device gone
        var sut = CreateSut(db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert
            Assert.Equal("pf-corp-id", graph.LastDeletedId);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that when <see cref="ICosmosDbService.UpdateDevice"/> throws
    /// <see cref="HttpStatusCode.PreconditionFailed"/> after a re-add and the re-fetched device
    /// has <see cref="DeviceStatus.Deleting"/> status, the newly created corp ID is rolled back.
    /// </summary>
    [Fact]
    public async Task Run_UpdateDevice_ThrowsPreconditionFailed_CorpIDReAdded_FreshDeviceDeleting_RollsBack()
    {
        // Arrange
        SetSyncEnabled();
        var device = MakeDevice(corpId: "");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService
        {
            OnAdd = (_, _) => Task.FromResult(new ImportedDeviceIdentity { Id = "del-corp-id", ImportedDeviceIdentifier = "ident" }),
        };
        db.OnUpdateDevice = _ => throw new CosmosException("precondition failed", HttpStatusCode.PreconditionFailed, 0, "act", 0.0);
        db.OnGetDevice = (_, _) => Task.FromResult<Device?>(new Device { Status = DeviceStatus.Deleting });
        var sut = CreateSut(db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert
            Assert.Equal("del-corp-id", graph.LastDeletedId);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that when <see cref="ICosmosDbService.UpdateDevice"/> throws
    /// <see cref="HttpStatusCode.PreconditionFailed"/> after a re-add and the re-fetched device
    /// has <see cref="DeviceStatus.NotSyncing"/> status, the newly created corp ID is rolled back.
    /// </summary>
    [Fact]
    public async Task Run_UpdateDevice_ThrowsPreconditionFailed_CorpIDReAdded_FreshDeviceNotSyncing_RollsBack()
    {
        // Arrange
        SetSyncEnabled();
        var device = MakeDevice(corpId: "");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService
        {
            OnAdd = (_, _) => Task.FromResult(new ImportedDeviceIdentity { Id = "ns-corp-id", ImportedDeviceIdentifier = "ident" }),
        };
        db.OnUpdateDevice = _ => throw new CosmosException("precondition failed", HttpStatusCode.PreconditionFailed, 0, "act", 0.0);
        db.OnGetDevice = (_, _) => Task.FromResult<Device?>(new Device { Status = DeviceStatus.NotSyncing });
        var sut = CreateSut(db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert
            Assert.Equal("ns-corp-id", graph.LastDeletedId);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that when <see cref="ICosmosDbService.UpdateDevice"/> throws
    /// <see cref="HttpStatusCode.PreconditionFailed"/> after a re-add and the re-fetched device
    /// is in an unexpected state (<see cref="DeviceStatus.Synced"/>), no rollback is performed
    /// and a warning is logged.
    /// </summary>
    [Fact]
    public async Task Run_UpdateDevice_ThrowsPreconditionFailed_CorpIDReAdded_UnexpectedState_NoRollback()
    {
        // Arrange
        SetSyncEnabled();
        var logFactory = new RecordingLoggerFactory();
        var device = MakeDevice(corpId: "");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService
        {
            OnAdd = (_, _) => Task.FromResult(new ImportedDeviceIdentity { Id = "ue-corp-id", ImportedDeviceIdentifier = "ident" }),
        };
        db.OnUpdateDevice = _ => throw new CosmosException("precondition failed", HttpStatusCode.PreconditionFailed, 0, "act", 0.0);
        db.OnGetDevice = (_, _) => Task.FromResult<Device?>(new Device { Status = DeviceStatus.Synced });
        var sut = CreateSut(loggerFactory: logFactory, db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert – no rollback; warning logged for unexpected state
            Assert.Null(graph.LastDeletedId);
            var warnings = logFactory.Logger.Logs
                .Where(l => l.Level == LogLevel.Warning)
                .Select(l => l.Message)
                .ToList();

            Assert.Contains(warnings, m => m.Contains("unexpected state"));
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that when <see cref="ICosmosDbService.UpdateDevice"/> throws
    /// <see cref="HttpStatusCode.PreconditionFailed"/> after a re-add and a subsequent
    /// <see cref="ICosmosDbService.GetDevice"/> call also throws, the exception is swallowed
    /// and processing continues to the next device.
    /// </summary>
    [Fact]
    public async Task Run_UpdateDevice_ThrowsPreconditionFailed_CorpIDReAdded_GetDeviceThrows_ContinuesToNextDevice()
    {
        // Arrange
        SetSyncEnabled();
        var device1 = MakeDevice(serial: "SN-001", corpId: "");
        var device2 = MakeDevice(serial: "SN-002", corpId: "existing-id");
        var db = MakeSyncDb(candidates: new List<Device> { device1, device2 });

        var addCallCount = 0;
        var graph = new StubGraphBetaService
        {
            OnAdd = (_, _) =>
            {
                addCallCount++;
                return Task.FromResult(new ImportedDeviceIdentity { Id = $"id-{addCallCount}", ImportedDeviceIdentifier = "ident" });
            },
            OnExists = _ => Task.FromResult(true),
        };

        var updateCallCount = 0;
        db.OnUpdateDevice = _ =>
        {
            updateCallCount++;
            if (updateCallCount == 1)
            {
                throw new CosmosException("precondition failed", HttpStatusCode.PreconditionFailed, 0, "act", 0.0);
            }

            return Task.CompletedTask;
        };
        db.OnGetDevice = (_, _) => throw new InvalidOperationException("fetch failed");

        var sut = CreateSut(db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert – second device was also processed (UpdateDevice called twice total)
            Assert.Equal(2, updateCallCount);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that when <see cref="ICosmosDbService.UpdateDevice"/> throws
    /// <see cref="HttpStatusCode.PreconditionFailed"/> and the prior corp ID re-add had also
    /// failed, no Graph rollback is attempted.
    /// </summary>
    [Fact]
    public async Task Run_UpdateDevice_ThrowsPreconditionFailed_CorpIDReAddFailed_NoRollback()
    {
        // Arrange
        SetSyncEnabled();
        var device = MakeDevice(corpId: "");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService
        {
            OnAdd = (_, _) => throw new InvalidOperationException("add error"),
        };
        db.OnUpdateDevice = _ => throw new CosmosException("precondition failed", HttpStatusCode.PreconditionFailed, 0, "act", 0.0);
        var sut = CreateSut(db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert – no rollback; add failed so nothing to roll back
            Assert.Null(graph.LastDeletedId);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Run – UpdateDevice generic exception
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that when <see cref="ICosmosDbService.UpdateDevice"/> throws a non-Cosmos
    /// exception in the corp-ID-found path, the failure is logged as an error.
    /// </summary>
    [Fact]
    public async Task Run_UpdateDevice_ThrowsGenericException_CorpIDFound_LogsException()
    {
        // Arrange
        SetSyncEnabled();
        var logFactory = new RecordingLoggerFactory();
        var device = MakeDevice(corpId: "existing-id");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService { OnExists = _ => Task.FromResult(true) };
        db.OnUpdateDevice = _ => throw new InvalidOperationException("db error");
        var sut = CreateSut(loggerFactory: logFactory, db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert
            var errors = logFactory.Logger.Logs
                .Where(l => l.Level == LogLevel.Error)
                .Select(l => l.Message)
                .ToList();

            Assert.Contains(errors, m => m.Contains("Failed to update device record"));
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that when <see cref="ICosmosDbService.UpdateDevice"/> throws a non-Cosmos
    /// exception after a successful re-add, a warning is logged indicating the re-added corp ID
    /// is now an orphan in Graph.
    /// </summary>
    [Fact]
    public async Task Run_UpdateDevice_ThrowsGenericException_CorpIDReAdded_LogsWarning()
    {
        // Arrange
        SetSyncEnabled();
        var logFactory = new RecordingLoggerFactory();
        var device = MakeDevice(corpId: "");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService
        {
            OnAdd = (_, _) => Task.FromResult(new ImportedDeviceIdentity { Id = "orphan-id", ImportedDeviceIdentifier = "ident" }),
        };
        db.OnUpdateDevice = _ => throw new InvalidOperationException("db error");
        var sut = CreateSut(loggerFactory: logFactory, db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert – warning logged about orphan Corp ID
            var warnings = logFactory.Logger.Logs
                .Where(l => l.Level == LogLevel.Warning)
                .Select(l => l.Message)
                .ToList();

            Assert.Contains(warnings, m => m.Contains("re-added Corp ID") && m.Contains("DB update failed"));
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that when both the corp ID re-add and the subsequent
    /// <see cref="ICosmosDbService.UpdateDevice"/> throw non-Cosmos exceptions, the DB update
    /// failure is logged as an error.
    /// </summary>
    [Fact]
    public async Task Run_UpdateDevice_ThrowsGenericException_CorpIDReAddFailed_LogsException()
    {
        // Arrange
        SetSyncEnabled();
        var logFactory = new RecordingLoggerFactory();
        var device = MakeDevice(corpId: "");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService
        {
            OnAdd = (_, _) => throw new InvalidOperationException("add error"),
        };
        db.OnUpdateDevice = _ => throw new InvalidOperationException("db error");
        var sut = CreateSut(loggerFactory: logFactory, db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert – exception logged about DB update failure
            var errors = logFactory.Logger.Logs
                .Where(l => l.Level == LogLevel.Error)
                .Select(l => l.Message)
                .ToList();

            Assert.Contains(errors, m => m.Contains("Failed to update device record"));
        }
        finally
        {
            ClearEnvVars();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Run – capacity release for failed re-adds
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that when one or more corp ID re-adds fail during a run, the capacity manager
    /// releases the corresponding number of Corp ID slots by decrementing the Cosmos DB counter.
    /// </summary>
    [Fact]
    public async Task Run_WhenReAddFailedCountPositive_ReleasesCorpIDsViaCapacityManager()
    {
        // Arrange
        SetSyncEnabled(maxCorpIds: 10000);
        var device = MakeDevice(corpId: "");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        db.Counter = new CorpIDCounter(5); // CorpIDCount = 5
        var graph = new StubGraphBetaService
        {
            OnAdd = (_, _) => throw new InvalidOperationException("graph unavailable"),
        };
        // UpdateDevice succeeds so countCorpIDsReAddFailed = 1
        var sut = CreateSut(db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());

            // Assert – ReleaseCorpIDs(1) was called; CorpIDCount decremented from 5 to 4
            Assert.Equal(4, db.Counter.CorpIDCount);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that when no corp ID re-adds failed during a run, the Corp ID counter is not
    /// decremented and the capacity release path is not triggered.
    /// </summary>
    [Fact]
    public async Task Run_WhenReAddFailedCountZero_DoesNotCallReleaseCorpIDs()
    {
        // Arrange
        SetSyncEnabled();
        var device = MakeDevice(corpId: "existing-id");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        db.Counter = new CorpIDCounter(5);
        var graph = new StubGraphBetaService { OnExists = _ => Task.FromResult(true) };
        var initialCallCount = 0;
        db.OnGetCorpIDCounter = () =>
        {
            initialCallCount++;
            return Task.FromResult(db.Counter);
        };
        var sut = CreateSut(db: db, graph: graph);

        try
        {
            // Act
            await sut.Run(new TimerInfo());
            var callCountAfterRun = initialCallCount;

            // Re-run with forced re-add failure to distinguish how many times GetCorpIDCounter is called
            // instead: just verify counter was not decremented (no release)
            Assert.Equal(5, db.Counter.CorpIDCount);
        }
        finally
        {
            ClearEnvVars();
        }
    }

    /// <summary>
    /// Verifies that when the capacity manager's release operation throws an exception, the error
    /// is logged and the exception is not rethrown — <see cref="ConfirmSync.Run"/> completes
    /// without propagating the failure.
    /// </summary>
    [Fact]
    public async Task Run_WhenReleaseCorpIDsThrows_LogsExceptionAndDoesNotRethrow()
    {
        // Arrange
        SetSyncEnabled(maxCorpIds: 10000);
        var logFactory = new RecordingLoggerFactory();
        var device = MakeDevice(corpId: "");
        var db = MakeSyncDb(candidates: new List<Device> { device });
        var graph = new StubGraphBetaService
        {
            OnAdd = (_, _) => throw new InvalidOperationException("graph down"),
        };
        db.OnGetCorpIDCounter = () => throw new InvalidOperationException("counter db error");
        var sut = CreateSut(loggerFactory: logFactory, db: db, graph: graph);

        try
        {
            // Act – should not throw
            await sut.Run(new TimerInfo());

            // Assert
            var errors = logFactory.Logger.Logs
                .Where(l => l.Level == LogLevel.Error)
                .Select(l => l.Message)
                .ToList();

            Assert.Contains(errors, m => m.Contains("Failed to release") && m.Contains("CorpID slots"));
        }
        finally
        {
            ClearEnvVars();
        }
    }
}
