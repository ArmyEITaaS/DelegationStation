using CorporateIdentifierSync.Enums;
using CorporateIdentifierSync.Interfaces;
using DelegationStationShared.Models;
using DelegationStationShared.Enums;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta.Models;
using System.Net;
using Xunit;
using Device = DelegationStationShared.Models.Device;
using DeviceTag = DelegationStationShared.Models.DeviceTag;

namespace CorporateIdentifierSync.Tests.ReconcileSyncStateTests;

[Collection("EnvVarTests")]
public class AddNotSyncingDevicesInEnabledTagsTests
{
    // ====================================================================
    // Helpers
    // ====================================================================

    private const string TagId = "tag-enabled-1";
    private const int InitialCorpIDCount = 5;

    private static Device MakeDevice() => new()
    {
        Id = Guid.NewGuid(),
        Make = "Dell",
        Model = "XPS",
        SerialNumber = "SN001",
        CorporateIdentityID = string.Empty,
        CorporateIdentity = string.Empty,
        Status = DeviceStatus.NotSyncing,
        PartitionKey = "pk-1",
        Tags = [TagId],
        OS = DeviceOS.Windows,
        CorpIDFailureCount = 0,
    };

    private static CosmosException CosmosPreconditionFailed()
        => new("Simulated 412", HttpStatusCode.PreconditionFailed, 0, Guid.NewGuid().ToString(), 0);

    private static CosmosException CosmosNotFound()
        => new("Simulated 404", HttpStatusCode.NotFound, 0, Guid.NewGuid().ToString(), 0);

    private static ReconcileSyncState CreateSut(
        Section2DbService db,
        Section2GraphService graph)
    {
        return new ReconcileSyncState(
            new SilentLoggerFactory(),
            db,
            graph,
            new FakeSingletonLock());
    }

    private static async Task RunSection2(Section2DbService db, Section2GraphService graph)
    {
        Environment.SetEnvironmentVariable("EnableCorpIDSync", "true");
        Environment.SetEnvironmentVariable("ReconcileSyncBatchSize", "100");
        Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", "5000");

        try
        {
            var sut = CreateSut(db, graph);
            await sut.Run(new TimerInfo());
        }
        finally
        {
            Environment.SetEnvironmentVariable("EnableCorpIDSync", null);
            Environment.SetEnvironmentVariable("ReconcileSyncBatchSize", null);
            Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", null);
        }
    }

    #region Row1_CorpIDAdded_DBUpdated
    // ====================================================================
    // Row 1: CorpID Added=Yes, DB Updated=Yes
    // Expected: CorpIDCount +1, Device Status=Synced with CorpID details added
    // ====================================================================

    /// <summary>
    /// CorpID successfully added to Graph; device successfully updated in Cosmos.
    /// Expected: counter incremented by 1, device set to Synced with CorpID details.
    /// </summary>
    [Fact]
    public async Task Row1_CorpIDAdded_DBUpdated_IncrementsCounterAndSyncsDevice()
    {
        // Arrange
        var device = MakeDevice();
        var db = new Section2DbService(device);
        var graph = new Section2GraphService();

        // Act
        await RunSection2(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount + 1, db.Counter.CorpIDCount);
        Assert.Equal(1, db.UpdateDeviceCallCount);
        Assert.Equal(DeviceStatus.Synced, db.LastUpdatedDevice!.Status);
        Assert.False(string.IsNullOrEmpty(db.LastUpdatedDevice.CorporateIdentityID));
        Assert.False(string.IsNullOrEmpty(db.LastUpdatedDevice.CorporateIdentity));
        Assert.Equal(0, db.LastUpdatedDevice.CorpIDFailureCount);
    }
    #endregion

    #region Row2_CorpIDAdded_DB404_RollbackSuccess
    // ====================================================================
    // Row 2: CorpID Added=Yes, DB Updated=No (404), Rollback=Yes
    // Expected: CorpIDCount no change, CorpID not present, device does not exist
    // ====================================================================

    /// <summary>
    /// CorpID added to Graph; Cosmos update returns 404 (device deleted during processing);
    /// rollback successfully removes CorpID from Graph.
    /// Expected: counter unchanged, CorpID rolled back.
    /// </summary>
    [Fact]
    public async Task Row2_CorpIDAdded_DB404_RollbackSuccess_NoCounterChange()
    {
        // Arrange
        var device = MakeDevice();
        var db = new Section2DbService(device)
        {
            UpdateDeviceException = CosmosNotFound(),
        };
        var graph = new Section2GraphService
        {
            RollbackDeleteResult = DeleteCorpIdResult.Success,
        };

        // Act
        await RunSection2(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount, db.Counter.CorpIDCount);
        Assert.Equal(1, graph.DeleteCorporateIdentifierCallCount);
    }
    #endregion

    #region Row3_CorpIDAdded_DB412_FreshNullOrDeleting_RollbackSuccess
    // ====================================================================
    // Row 3: CorpID Added=Yes, DB Updated=No (412), Fresh=Null/Deleting, Rollback=Yes
    // Expected: CorpIDCount no change, CorpID not present, device does not exist / not changed
    // ====================================================================

    /// <summary>
    /// CorpID added to Graph; Cosmos 412; fresh device is null (deleted) or Deleting;
    /// rollback successfully removes CorpID from Graph.
    /// Expected: counter unchanged.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(DeviceStatus.Deleting)]
    public async Task Row3_CorpIDAdded_DB412_FreshNullOrDeleting_RollbackSuccess_NoCounterChange(DeviceStatus? freshStatus)
    {
        // Arrange
        var device = MakeDevice();
        Device? freshDevice = freshStatus.HasValue
            ? new Device
            {
                Id = device.Id, Make = device.Make, Model = device.Model,
                SerialNumber = device.SerialNumber, PartitionKey = device.PartitionKey,
                Tags = [TagId], Status = freshStatus.Value,
            }
            : null;

        var db = new Section2DbService(device)
        {
            UpdateDeviceException = CosmosPreconditionFailed(),
            FreshDevice = freshDevice,
        };
        var graph = new Section2GraphService
        {
            RollbackDeleteResult = DeleteCorpIdResult.Success,
        };

        // Act
        await RunSection2(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount, db.Counter.CorpIDCount);
        Assert.Equal(1, graph.DeleteCorporateIdentifierCallCount);
    }
    #endregion

    #region Row4_CorpIDAdded_DB412_FreshOther_RollbackSuccess
    // ====================================================================
    // Row 4: CorpID Added=Yes, DB Updated=No (412), Fresh=Other (not expected), Rollback=Yes
    // Expected: CorpIDCount no change, CorpID not present, device not changed
    // ====================================================================

    /// <summary>
    /// CorpID added to Graph; Cosmos 412; fresh device has an unexpected status (defensive fallback);
    /// rollback successfully removes CorpID from Graph.
    /// Expected: counter unchanged.
    /// </summary>
    [Fact]
    public async Task Row4_CorpIDAdded_DB412_FreshOtherStatus_RollbackSuccess_NoCounterChange()
    {
        // Arrange
        var device = MakeDevice();
        var freshDevice = new Device
        {
            Id = device.Id, Make = device.Make, Model = device.Model,
            SerialNumber = device.SerialNumber, PartitionKey = device.PartitionKey,
            Tags = [TagId], Status = DeviceStatus.Synced, // unexpected state
        };

        var db = new Section2DbService(device)
        {
            UpdateDeviceException = CosmosPreconditionFailed(),
            FreshDevice = freshDevice,
        };
        var graph = new Section2GraphService
        {
            RollbackDeleteResult = DeleteCorpIdResult.Success,
        };

        // Act
        await RunSection2(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount, db.Counter.CorpIDCount);
        Assert.Equal(1, graph.DeleteCorporateIdentifierCallCount);
    }
    #endregion

    #region Row5_CorpIDAdded_DB412_GetDeviceThrows_RollbackSuccess
    // ====================================================================
    // Row 5: CorpID Added=Yes, DB Updated=No (412), GetDevice errored, Rollback=Yes
    // Expected: CorpIDCount no change, CorpID not present, device not changed
    // ====================================================================

    /// <summary>
    /// CorpID added to Graph; Cosmos 412; re-read of device throws;
    /// rollback successfully removes CorpID from Graph.
    /// Expected: counter unchanged.
    /// </summary>
    [Fact]
    public async Task Row5_CorpIDAdded_DB412_GetDeviceThrows_RollbackSuccess_NoCounterChange()
    {
        // Arrange
        var device = MakeDevice();
        var db = new Section2DbService(device)
        {
            UpdateDeviceException = CosmosPreconditionFailed(),
            GetDeviceException = new Exception("Cosmos read failure"),
        };
        var graph = new Section2GraphService
        {
            RollbackDeleteResult = DeleteCorpIdResult.Success,
        };

        // Act
        await RunSection2(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount, db.Counter.CorpIDCount);
        Assert.Equal(1, graph.DeleteCorporateIdentifierCallCount);
    }
    #endregion

    #region Row6_CorpIDAdded_DBOtherException_RollbackSuccess
    // ====================================================================
    // Row 6: CorpID Added=Yes, DB Updated=No (other exception), Rollback=Yes
    // Expected: CorpIDCount no change, CorpID not present, device not changed
    // ====================================================================

    /// <summary>
    /// CorpID added to Graph; Cosmos update throws a non-404/non-412 exception;
    /// rollback successfully removes CorpID from Graph.
    /// Expected: counter unchanged.
    /// </summary>
    [Fact]
    public async Task Row6_CorpIDAdded_DBOtherException_RollbackSuccess_NoCounterChange()
    {
        // Arrange
        var device = MakeDevice();
        var db = new Section2DbService(device)
        {
            UpdateDeviceException = new InvalidOperationException("Unexpected Cosmos failure"),
        };
        var graph = new Section2GraphService
        {
            RollbackDeleteResult = DeleteCorpIdResult.Success,
        };

        // Act
        await RunSection2(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount, db.Counter.CorpIDCount);
        Assert.Equal(1, graph.DeleteCorporateIdentifierCallCount);
    }
    #endregion

    #region Row7_CorpIDAdded_DB404_RollbackFailed
    // ====================================================================
    // Row 7: CorpID Added=Yes, DB Updated=No (404), Rollback=No
    // Expected: CorpIDCount +1 (orphaned), CorpID still present, device does not exist
    // ====================================================================

    /// <summary>
    /// CorpID added to Graph; Cosmos 404; rollback FAILS — CorpID is orphaned in Graph.
    /// Expected: counter incremented (addedCount not decremented).
    /// </summary>
    [Fact]
    public async Task Row7_CorpIDAdded_DB404_RollbackFailed_CounterIncrementedOrphaned()
    {
        // Arrange
        var device = MakeDevice();
        var db = new Section2DbService(device)
        {
            UpdateDeviceException = CosmosNotFound(),
        };
        var graph = new Section2GraphService
        {
            RollbackDeleteResult = DeleteCorpIdResult.Error,
        };

        // Act
        await RunSection2(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount + 1, db.Counter.CorpIDCount);
        Assert.Equal(1, graph.DeleteCorporateIdentifierCallCount);
    }
    #endregion

    #region Row8_CorpIDAdded_DB412_FreshNullOrDeleting_RollbackFailed
    // ====================================================================
    // Row 8: CorpID Added=Yes, DB Updated=No (412), Fresh=Null/Deleting, Rollback=No
    // Expected: CorpIDCount +1 (orphaned), CorpID still present, device not changed
    // ====================================================================

    /// <summary>
    /// CorpID added to Graph; Cosmos 412; fresh device is null/Deleting;
    /// rollback FAILS — CorpID is orphaned.
    /// Expected: counter incremented.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(DeviceStatus.Deleting)]
    public async Task Row8_CorpIDAdded_DB412_FreshNullOrDeleting_RollbackFailed_CounterIncremented(DeviceStatus? freshStatus)
    {
        // Arrange
        var device = MakeDevice();
        Device? freshDevice = freshStatus.HasValue
            ? new Device
            {
                Id = device.Id, Make = device.Make, Model = device.Model,
                SerialNumber = device.SerialNumber, PartitionKey = device.PartitionKey,
                Tags = [TagId], Status = freshStatus.Value,
            }
            : null;

        var db = new Section2DbService(device)
        {
            UpdateDeviceException = CosmosPreconditionFailed(),
            FreshDevice = freshDevice,
        };
        var graph = new Section2GraphService
        {
            RollbackDeleteResult = DeleteCorpIdResult.Error,
        };

        // Act
        await RunSection2(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount + 1, db.Counter.CorpIDCount);
    }
    #endregion

    #region Row9_CorpIDAdded_DB412_FreshOther_RollbackFailed
    // ====================================================================
    // Row 9: CorpID Added=Yes, DB Updated=No (412), Fresh=Other (not expected), Rollback=No
    // Expected: CorpIDCount +1 (orphaned), CorpID still present, device not changed
    // ====================================================================

    /// <summary>
    /// CorpID added to Graph; Cosmos 412; fresh device in unexpected status;
    /// rollback FAILS — CorpID is orphaned.
    /// Expected: counter incremented.
    /// </summary>
    [Fact]
    public async Task Row9_CorpIDAdded_DB412_FreshOther_RollbackFailed_CounterIncremented()
    {
        // Arrange
        var device = MakeDevice();
        var freshDevice = new Device
        {
            Id = device.Id, Make = device.Make, Model = device.Model,
            SerialNumber = device.SerialNumber, PartitionKey = device.PartitionKey,
            Tags = [TagId], Status = DeviceStatus.Synced,
        };

        var db = new Section2DbService(device)
        {
            UpdateDeviceException = CosmosPreconditionFailed(),
            FreshDevice = freshDevice,
        };
        var graph = new Section2GraphService
        {
            RollbackDeleteResult = DeleteCorpIdResult.Error,
        };

        // Act
        await RunSection2(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount + 1, db.Counter.CorpIDCount);
    }
    #endregion

    #region Row10_CorpIDAdded_DB412_GetDeviceThrows_RollbackFailed
    // ====================================================================
    // Row 10: CorpID Added=Yes, DB Updated=No (412), GetDevice errored, Rollback=No
    // Expected: CorpIDCount +1 (orphaned), CorpID still present, device not changed
    // ====================================================================

    /// <summary>
    /// CorpID added to Graph; Cosmos 412; re-read throws;
    /// rollback FAILS — CorpID is orphaned.
    /// Expected: counter incremented.
    /// </summary>
    [Fact]
    public async Task Row10_CorpIDAdded_DB412_GetDeviceThrows_RollbackFailed_CounterIncremented()
    {
        // Arrange
        var device = MakeDevice();
        var db = new Section2DbService(device)
        {
            UpdateDeviceException = CosmosPreconditionFailed(),
            GetDeviceException = new Exception("Cosmos read failure"),
        };
        var graph = new Section2GraphService
        {
            RollbackDeleteResult = DeleteCorpIdResult.Error,
        };

        // Act
        await RunSection2(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount + 1, db.Counter.CorpIDCount);
    }
    #endregion

    #region Row11_CorpIDAdded_DBOtherException_RollbackFailed
    // ====================================================================
    // Row 11: CorpID Added=Yes, DB Updated=No (other exception), Rollback=No
    // Expected: CorpIDCount +1 (orphaned), CorpID still present, device not changed
    // ====================================================================

    /// <summary>
    /// CorpID added to Graph; Cosmos throws generic exception;
    /// rollback FAILS — CorpID is orphaned.
    /// Expected: counter incremented.
    /// </summary>
    [Fact]
    public async Task Row11_CorpIDAdded_DBOtherException_RollbackFailed_CounterIncremented()
    {
        // Arrange
        var device = MakeDevice();
        var db = new Section2DbService(device)
        {
            UpdateDeviceException = new InvalidOperationException("Unexpected Cosmos failure"),
        };
        var graph = new Section2GraphService
        {
            RollbackDeleteResult = DeleteCorpIdResult.Error,
        };

        // Act
        await RunSection2(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount + 1, db.Counter.CorpIDCount);
    }
    #endregion

    #region Row12_GraphAddException_DBUpdated
    // ====================================================================
    // Row 12: CorpID Added=No (Exception), DB Updated=Yes
    // Expected: CorpIDCount no change, CorpID not created,
    //           Device Status=Added, FailureCount++
    // ====================================================================

    /// <summary>
    /// Graph AddCorporateIdentifier throws; device updated with failure state.
    /// Expected: counter unchanged, device persisted as Added with CorpIDFailureCount incremented.
    /// </summary>
    [Fact]
    public async Task Row12_GraphAddException_DBUpdated_NoCounterChangeDeviceMarkedFailed()
    {
        // Arrange
        var device = MakeDevice();
        var db = new Section2DbService(device);
        var graph = new Section2GraphService
        {
            AddException = new Exception("Graph API failure"),
        };

        // Act
        await RunSection2(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount, db.Counter.CorpIDCount);
        Assert.Equal(1, db.UpdateDeviceCallCount);
        Assert.Equal(DeviceStatus.Added, db.LastUpdatedDevice!.Status);
        Assert.Equal(1, db.LastUpdatedDevice.CorpIDFailureCount);
        Assert.Equal(string.Empty, db.LastUpdatedDevice.CorporateIdentityID);
    }
    #endregion

    // ====================================================================
    // Inner fakes
    // ====================================================================

    private sealed class SilentLoggerFactory : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider) { }
        public ILogger CreateLogger(string categoryName) => new SilentLogger();
        public void Dispose() { }

        private sealed class SilentLogger : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter) { }
        }
    }

    private sealed class FakeSingletonLock : IFunctionSingletonLock
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(string lockName, CancellationToken cancellationToken = default)
            => Task.FromResult<IAsyncDisposable?>(new FakeHandle());

        private sealed class FakeHandle : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Configurable fake for <see cref="IGraphBetaService"/> supporting both Add and Delete (rollback) scenarios.
    /// </summary>
    private sealed class Section2GraphService : IGraphBetaService
    {
        /// <summary>Exception thrown by AddCorporateIdentifier. If null, returns a valid identity.</summary>
        public Exception? AddException { get; set; }

        /// <summary>Result returned by DeleteCorporateIdentifier (used for rollback).</summary>
        public DeleteCorpIdResult RollbackDeleteResult { get; set; } = DeleteCorpIdResult.Success;

        /// <summary>Tracks how many times DeleteCorporateIdentifier was called (rollback attempts).</summary>
        public int DeleteCorporateIdentifierCallCount { get; private set; }

        public Task<ImportedDeviceIdentity> AddCorporateIdentifier(
            ImportedDeviceIdentityType type, string identifier)
        {
            if (AddException is not null) throw AddException;

            return Task.FromResult(new ImportedDeviceIdentity
            {
                Id = "corp-id-new-001",
                ImportedDeviceIdentifier = identifier,
            });
        }

        public Task<DeleteCorpIdResult> DeleteCorporateIdentifier(string identifierID)
        {
            DeleteCorporateIdentifierCallCount++;
            return Task.FromResult(RollbackDeleteResult);
        }

        public Task<bool> CorporateIdentifierExists(string identiferID)
            => throw new NotImplementedException();
        public Task<int> GetCorporateDeviceIdentifierCountAsync()
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Configurable fake for <see cref="ICosmosDbService"/> designed for AddNotSyncingDevicesInEnabledTags scenarios.
    /// Section 1 is configured as a no-op (empty non-syncing tags). Section 2 drives the test scenario.
    /// </summary>
    private sealed class Section2DbService : ICosmosDbService
    {
        private readonly Device _device;

        public Section2DbService(Device device)
        {
            _device = device;
            Counter = new CorpIDCounter(0) { CorpIDCount = InitialCorpIDCount };
        }

        // --- Configurable behavior ---

        /// <summary>Exception thrown by UpdateDevice. If null, succeeds.</summary>
        public Exception? UpdateDeviceException { get; set; }

        /// <summary>Device returned by GetDevice (re-read after 412). Null simulates deleted device.</summary>
        public Device? FreshDevice { get; set; }

        /// <summary>Exception thrown by GetDevice. Simulates read failure after 412.</summary>
        public Exception? GetDeviceException { get; set; }

        /// <summary>Counter used by CorpIdCapacityManager for reserve/commit operations.</summary>
        public CorpIDCounter Counter { get; set; }

        // --- Tracking ---

        public int UpdateDeviceCallCount { get; private set; }
        public Device? LastUpdatedDevice { get; private set; }

        // --- Section 1: no-op ---

        public Task<List<string>> GetNonSyncingDeviceTags()
            => Task.FromResult(new List<string>());

        public Task<List<Device>> GetSyncedDevicesInTags(List<string> tagIds, int batchSize)
            => Task.FromResult(new List<Device>());

        // --- Section 2: active ---

        public Task<List<string>> GetSyncingDeviceTags()
            => Task.FromResult(new List<string> { TagId });

        public Task<List<Device>> GetNotSyncingDevicesInTags(List<string> tags, int batchSize)
            => Task.FromResult(new List<Device> { _device });

        public Task UpdateDevice(Device device)
        {
            UpdateDeviceCallCount++;
            LastUpdatedDevice = device;
            if (UpdateDeviceException is not null) throw UpdateDeviceException;
            return Task.CompletedTask;
        }

        public Task<Device?> GetDevice(Guid id, string partitionKey)
        {
            if (GetDeviceException is not null) throw GetDeviceException;
            return Task.FromResult(FreshDevice);
        }

        // --- Capacity manager ---

        public Task<CorpIDCounter> GetCorpIDCounter()
            => Task.FromResult(Counter);

        public Task<bool> TrySetCorpIDCounter(CorpIDCounter counter, string etag)
        {
            Counter = counter;
            return Task.FromResult(true);
        }

        // --- Not used ---
        public Task<List<Device>> GetAddedDevices(int batchSize) => throw new NotImplementedException();
        public Task<List<Device>> GetAddedDevicesNotSyncing(List<string> tagIds, int batchSize) => throw new NotImplementedException();
        public Task<List<Device>> GetAddedDevicesToSync(List<string> tagIds, int batchSize) => throw new NotImplementedException();
        public Task<List<Device>> GetDevicesMarkedForDeletion() => throw new NotImplementedException();
        public Task DeleteDevice(Device device) => throw new NotImplementedException();
        public Task<List<Device>> GetDevicesSyncedBefore(DateTime date) => throw new NotImplementedException();
        public Task<List<Device>> GetSyncedDevicesSyncedBefore(DateTime date) => throw new NotImplementedException();
        public Task<DeviceTag> GetDeviceTag(string id) => throw new NotImplementedException();
        public Task<int> GetSyncedDeviceCountAsync() => throw new NotImplementedException();
    }
}