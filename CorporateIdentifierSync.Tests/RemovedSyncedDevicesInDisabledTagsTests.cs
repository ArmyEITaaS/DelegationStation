using CorporateIdentifierSync.Enums;
using CorporateIdentifierSync.Interfaces;
using DelegationStationShared.Models;
using DelegationStationShared.Enums;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Net;
using Xunit;
using Device = DelegationStationShared.Models.Device;
using DeviceTag = DelegationStationShared.Models.DeviceTag;
using Microsoft.Graph.Beta.Models;

namespace CorporateIdentifierSync.Tests.ReconcileSyncStateTests;

[Collection("EnvVarTests")]
public class RemoveSyncedDevicesInDisabledTagsTests
{
    // ====================================================================
    // Helpers
    // ====================================================================

    private const string TagId = "tag-disabled-1";
    private const int InitialCorpIDCount = 5;

    private static Device MakeDevice(string corpIdPresent = "corp-id-001") => new()
    {
        Id = Guid.NewGuid(),
        Make = "Dell",
        Model = "XPS",
        SerialNumber = "SN001",
        CorporateIdentityID = corpIdPresent,
        CorporateIdentity = "identity-hash",
        Status = DeviceStatus.Synced,
        PartitionKey = "pk-1",
        Tags = [TagId],
    };

    private static CosmosException CosmosPreconditionFailed()
        => new("Simulated 412", HttpStatusCode.PreconditionFailed, 0, Guid.NewGuid().ToString(), 0);

    private static CosmosException CosmosNotFound()
        => new("Simulated 404", HttpStatusCode.NotFound, 0, Guid.NewGuid().ToString(), 0);

    private static ReconcileSyncState CreateSut(
        ScenarioDbService db,
        ScenarioGraphService? graph = null)
    {
        return new ReconcileSyncState(
            new SilentLoggerFactory(),
            db,
            graph ?? new ScenarioGraphService(DeleteCorpIdResult.Success),
            new FakeSingletonLock());
    }

    private static async Task RunSection1(ScenarioDbService db, ScenarioGraphService? graph = null)
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

    #region Row1_CorpIDRemoved_DBUpdated
    // ====================================================================
    // Row 1: CorpID Present=Yes, Removed=Yes, DB Updated=Yes
    // Expected: CorpIDCount -1, Device State=NotSyncing with CorpID cleared
    // ====================================================================

    /// <summary>
    /// CorpID successfully deleted from Graph; device successfully updated in Cosmos.
    /// Expected: counter decremented by 1, device set to NotSyncing with CorpID details removed.
    /// </summary>
    [Fact]
    public async Task Row1_CorpIDRemoved_DBUpdated_DecrementsCounterAndClearsDevice()
    {
        // Arrange
        var device = MakeDevice();
        var db = new ScenarioDbService(device);
        var graph = new ScenarioGraphService(DeleteCorpIdResult.Success);

        // Act
        await RunSection1(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount - 1, db.Counter.CorpIDCount);
        Assert.Equal(1, db.UpdateDeviceCallCount);
        Assert.Equal(DeviceStatus.NotSyncing, db.LastUpdatedDevice!.Status);
        Assert.Equal(string.Empty, db.LastUpdatedDevice.CorporateIdentityID);
        Assert.Equal(string.Empty, db.LastUpdatedDevice.CorporateIdentity);
    }
    #endregion

    #region Row2_CorpIDRemoved_DB404
    // ====================================================================
    // Row 2: CorpID Present=Yes, Removed=Yes, DB Updated=No (404)
    // Expected: CorpIDCount -1, Device does not exist
    // ====================================================================

    /// <summary>
    /// CorpID successfully deleted from Graph; Cosmos update returns 404 (device already deleted).
    /// Expected: counter still decremented by 1 (we own the capacity release).
    /// </summary>
    [Fact]
    public async Task Row2_CorpIDRemoved_DB404_DecrementsCounter()
    {
        // Arrange
        var device = MakeDevice();
        var db = new ScenarioDbService(device)
        {
            UpdateDeviceException = CosmosNotFound(),
        };
        var graph = new ScenarioGraphService(DeleteCorpIdResult.Success);

        // Act
        await RunSection1(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount - 1, db.Counter.CorpIDCount);
    }
    #endregion

    #region Row3_CorpIDRemoved_DB412_FreshDeviceNullOrDeletingOrNotSyncing
    // ====================================================================
    // Row 3: CorpID Present=Yes, Removed=Yes, DB Updated=No (412),
    //         Fresh device is null/Deleting/NotSyncing
    // Expected: CorpIDCount -1, leave device state as-is
    // ====================================================================

    /// <summary>
    /// CorpID deleted from Graph; Cosmos 412 on update; fresh device is already in an end state
    /// (null, Deleting, or NotSyncing). Counter is still decremented because we performed the Graph delete.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(DeviceStatus.Deleting)]
    [InlineData(DeviceStatus.NotSyncing)]
    public async Task Row3_CorpIDRemoved_DB412_FreshDeviceInEndState_DecrementsCounterLeavesDevice(DeviceStatus? freshStatus)
    {
        // Arrange
        var device = MakeDevice();
        Device? freshDevice = freshStatus.HasValue
            ? new Device
            {
                Id = device.Id,
                Make = device.Make,
                Model = device.Model,
                SerialNumber = device.SerialNumber,
                PartitionKey = device.PartitionKey,
                Tags = [TagId],
                Status = freshStatus.Value,
                CorporateIdentityID = device.CorporateIdentityID,
                CorporateIdentity = device.CorporateIdentity,
            }
            : null;

        var db = new ScenarioDbService(device)
        {
            UpdateDeviceException = CosmosPreconditionFailed(),
            FreshDevice = freshDevice,
        };
        var graph = new ScenarioGraphService(DeleteCorpIdResult.Success);

        // Act
        await RunSection1(db, graph);

        // Assert — counter decremented; no second update attempted
        Assert.Equal(InitialCorpIDCount - 1, db.Counter.CorpIDCount);
        Assert.Equal(1, db.UpdateDeviceCallCount); // only the first (failed) call
    }
    #endregion

    #region Row4_CorpIDRemoved_DB412_FreshSynced_TagDisabled
    // ====================================================================
    // Row 4: CorpID Present=Yes, Removed=Yes, DB Updated=No (412),
    //         Fresh device is Synced/Added/Failed, Tag still disabled
    // Expected: CorpIDCount -1, Device State=NotSyncing with CorpID cleared
    // ====================================================================

    /// <summary>
    /// CorpID deleted from Graph; Cosmos 412; fresh device is Synced/Added/Failed; tag is still disabled.
    /// Retry update succeeds. Counter decremented, device set to NotSyncing.
    /// </summary>
    [Theory]
    [InlineData(DeviceStatus.Synced)]
    [InlineData(DeviceStatus.Added)]
    [InlineData(DeviceStatus.Failed)]
    public async Task Row4_CorpIDRemoved_DB412_FreshSyncedTagDisabled_DecrementsAndClearsDevice(DeviceStatus freshStatus)
    {
        // Arrange
        var device = MakeDevice();
        var freshDevice = new Device
        {
            Id = device.Id,
            Make = device.Make,
            Model = device.Model,
            SerialNumber = device.SerialNumber,
            PartitionKey = device.PartitionKey,
            Tags = [TagId],
            Status = freshStatus,
            CorporateIdentityID = "corp-id-001",
            CorporateIdentity = "identity-hash",
        };

        var db = new ScenarioDbService(device)
        {
            UpdateDeviceException = CosmosPreconditionFailed(),
            FreshDevice = freshDevice,
            NonSyncingTagsOnRecheck = [TagId], // tag still disabled
        };
        var graph = new ScenarioGraphService(DeleteCorpIdResult.Success);

        // Act
        await RunSection1(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount - 1, db.Counter.CorpIDCount);
        Assert.Equal(2, db.UpdateDeviceCallCount); // first (412) + retry
        Assert.Equal(DeviceStatus.NotSyncing, db.LastUpdatedDevice!.Status);
        Assert.Equal(string.Empty, db.LastUpdatedDevice.CorporateIdentityID);
        Assert.Equal(string.Empty, db.LastUpdatedDevice.CorporateIdentity);
    }
    #endregion

    #region Row5_CorpIDRemoved_DB412_FreshSynced_TagEnabled
    // ====================================================================
    // Row 5: CorpID Present=Yes, Removed=Yes, DB Updated=No (412),
    //         Fresh device is Synced/Added/Failed, Tag re-enabled
    // Expected: CorpIDCount no change, leave device state
    // ====================================================================

    /// <summary>
    /// CorpID deleted from Graph; Cosmos 412; fresh device is Synced; tag was re-enabled.
    /// ConfirmSync will re-add the CorpID. Counter NOT decremented, device NOT updated.
    /// </summary>
    [Fact]
    public async Task Row5_CorpIDRemoved_DB412_FreshSynced_TagReEnabled_NoCounterChangeNoUpdate()
    {
        // Arrange
        var device = MakeDevice();
        var freshDevice = new Device
        {
            Id = device.Id,
            Make = device.Make,
            Model = device.Model,
            SerialNumber = device.SerialNumber,
            PartitionKey = device.PartitionKey,
            Tags = [TagId],
            Status = DeviceStatus.Synced,
            CorporateIdentityID = "corp-id-001",
            CorporateIdentity = "identity-hash",
        };

        var db = new ScenarioDbService(device)
        {
            UpdateDeviceException = CosmosPreconditionFailed(),
            FreshDevice = freshDevice,
            NonSyncingTagsOnRecheck = [], // tag no longer in disabled list
        };
        var graph = new ScenarioGraphService(DeleteCorpIdResult.Success);

        // Act
        await RunSection1(db, graph);

        // Assert — counter unchanged, only 1 UpdateDevice call (the one that threw 412)
        Assert.Equal(InitialCorpIDCount, db.Counter.CorpIDCount);
        Assert.Equal(1, db.UpdateDeviceCallCount);
    }
    #endregion

    #region Row6_CorpIDRemoved_DB412_FreshSynced_TagCheckErrored
    // ====================================================================
    // Row 6: CorpID Present=Yes, Removed=Yes, DB Updated=No (412),
    //         Fresh device is Synced/Added/Failed, Tag re-check throws
    // Expected: CorpIDCount no change, no change to device
    // ====================================================================

    /// <summary>
    /// CorpID deleted from Graph; Cosmos 412; fresh device is Synced; tag re-check throws.
    /// Deferred to next run. Counter unchanged, device unchanged.
    /// </summary>
    [Fact]
    public async Task Row6_CorpIDRemoved_DB412_FreshSynced_TagCheckFails_NoChangeDeferred()
    {
        // Arrange
        var device = MakeDevice();
        var freshDevice = new Device
        {
            Id = device.Id,
            Make = device.Make,
            Model = device.Model,
            SerialNumber = device.SerialNumber,
            PartitionKey = device.PartitionKey,
            Tags = [TagId],
            Status = DeviceStatus.Synced,
            CorporateIdentityID = "corp-id-001",
            CorporateIdentity = "identity-hash",
        };

        var db = new ScenarioDbService(device)
        {
            UpdateDeviceException = CosmosPreconditionFailed(),
            FreshDevice = freshDevice,
            NonSyncingTagsRecheckException = new Exception("Cosmos read failure"),
        };
        var graph = new ScenarioGraphService(DeleteCorpIdResult.Success);

        // Act
        await RunSection1(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount, db.Counter.CorpIDCount);
        Assert.Equal(1, db.UpdateDeviceCallCount);
    }
    #endregion

    #region Row7_CorpIDRemoved_DB412_GetDeviceThrows
    // ====================================================================
    // Row 7: CorpID Present=Yes, Removed=Yes, DB Updated=No (412),
    //         GetDevice throws (Errored)
    // Expected: CorpIDCount no change, no change to device
    // ====================================================================

    /// <summary>
    /// CorpID deleted from Graph; Cosmos 412; re-read of device throws.
    /// Deferred to next run. Counter unchanged, device unchanged.
    /// </summary>
    [Fact]
    public async Task Row7_CorpIDRemoved_DB412_GetDeviceThrows_NoChangeDeferred()
    {
        // Arrange
        var device = MakeDevice();
        var db = new ScenarioDbService(device)
        {
            UpdateDeviceException = CosmosPreconditionFailed(),
            GetDeviceException = new Exception("Cosmos read failure"),
        };
        var graph = new ScenarioGraphService(DeleteCorpIdResult.Success);

        // Act
        await RunSection1(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount, db.Counter.CorpIDCount);
        Assert.Equal(1, db.UpdateDeviceCallCount);
    }
    #endregion

    #region Row8_CorpIDRemoved_DBOtherException
    // ====================================================================
    // Row 8: CorpID Present=Yes, Removed=Yes, DB Updated=No (other exception)
    // Expected: CorpIDCount no change, no change to device
    // ====================================================================

    /// <summary>
    /// CorpID deleted from Graph; Cosmos update throws a non-404/non-412 exception.
    /// Counter NOT decremented (can't confirm outcome ownership). Device unchanged.
    /// </summary>
    [Fact]
    public async Task Row8_CorpIDRemoved_DBOtherException_NoChange()
    {
        // Arrange
        var device = MakeDevice();
        var db = new ScenarioDbService(device)
        {
            UpdateDeviceException = new InvalidOperationException("Unexpected Cosmos failure"),
        };
        var graph = new ScenarioGraphService(DeleteCorpIdResult.Success);

        // Act
        await RunSection1(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount, db.Counter.CorpIDCount);
    }
    #endregion

    #region Row9_CorpIDNotFound_DBUpdated
    // ====================================================================
    // Row 9: CorpID Present=Yes, Removed=No (NotFound), DB Updated=Yes
    // Expected: CorpIDCount no change, Device State=NotSyncing with CorpID cleared
    // ====================================================================

    /// <summary>
    /// CorpID not found in Graph (already gone); device successfully updated in Cosmos.
    /// Counter unchanged (we didn't perform the actual delete). Device set to NotSyncing.
    /// </summary>
    [Fact]
    public async Task Row9_CorpIDNotFound_DBUpdated_NoCounterChange_ClearsDevice()
    {
        // Arrange
        var device = MakeDevice();
        var db = new ScenarioDbService(device);
        var graph = new ScenarioGraphService(DeleteCorpIdResult.NotFound);

        // Act
        await RunSection1(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount, db.Counter.CorpIDCount);
        Assert.Equal(1, db.UpdateDeviceCallCount);
        Assert.Equal(DeviceStatus.NotSyncing, db.LastUpdatedDevice!.Status);
        Assert.Equal(string.Empty, db.LastUpdatedDevice.CorporateIdentityID);
        Assert.Equal(string.Empty, db.LastUpdatedDevice.CorporateIdentity);
    }
    #endregion

    #region Row10_CorpIDNotFound_DB404
    // ====================================================================
    // Row 10: CorpID Present=Yes, Removed=No (NotFound), DB Updated=No (404)
    // Expected: CorpIDCount no change, Device does not exist
    // ====================================================================

    /// <summary>
    /// CorpID not found in Graph; Cosmos update returns 404 (device already deleted).
    /// Counter unchanged, device already gone.
    /// </summary>
    [Fact]
    public async Task Row10_CorpIDNotFound_DB404_NoCounterChange()
    {
        // Arrange
        var device = MakeDevice();
        var db = new ScenarioDbService(device)
        {
            UpdateDeviceException = CosmosNotFound(),
        };
        var graph = new ScenarioGraphService(DeleteCorpIdResult.NotFound);

        // Act
        await RunSection1(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount, db.Counter.CorpIDCount);
        Assert.Equal(1, db.UpdateDeviceCallCount);
    }
    #endregion

    #region Row11_CorpIDNotFound_DB412_FreshDeviceEndState
    // ====================================================================
    // Row 11: CorpID Present=Yes, Removed=No (NotFound), DB Updated=No (412),
    //          Fresh device is null/Deleting/NotSyncing
    // Expected: CorpIDCount no change, leave device state
    // ====================================================================

    /// <summary>
    /// CorpID not found in Graph; Cosmos 412; fresh device already in end state.
    /// Counter unchanged (no Graph delete performed). Device left as-is.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(DeviceStatus.Deleting)]
    [InlineData(DeviceStatus.NotSyncing)]
    public async Task Row11_CorpIDNotFound_DB412_FreshDeviceEndState_NoChange(DeviceStatus? freshStatus)
    {
        // Arrange
        var device = MakeDevice();
        Device? freshDevice = freshStatus.HasValue
            ? new Device
            {
                Id = device.Id,
                Make = device.Make,
                Model = device.Model,
                SerialNumber = device.SerialNumber,
                PartitionKey = device.PartitionKey,
                Tags = [TagId],
                Status = freshStatus.Value,
                CorporateIdentityID = device.CorporateIdentityID,
                CorporateIdentity = device.CorporateIdentity,
            }
            : null;

        var db = new ScenarioDbService(device)
        {
            UpdateDeviceException = CosmosPreconditionFailed(),
            FreshDevice = freshDevice,
        };
        var graph = new ScenarioGraphService(DeleteCorpIdResult.NotFound);

        // Act
        await RunSection1(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount, db.Counter.CorpIDCount);
        Assert.Equal(1, db.UpdateDeviceCallCount); // only the failed first call
    }
    #endregion

    #region Row12_CorpIDNotFound_DB412_FreshSynced_TagDisabled
    // ====================================================================
    // Row 12: CorpID Present=Yes, Removed=No (NotFound), DB Updated=No (412),
    //          Fresh device is Synced/Added/Failed, Tag still disabled
    // Expected: CorpIDCount no change, Device State=NotSyncing with CorpID cleared
    // ====================================================================

    /// <summary>
    /// CorpID not found in Graph; Cosmos 412; fresh device is Synced; tag still disabled.
    /// Retry update succeeds. Counter unchanged, but device cleared.
    /// </summary>
    [Theory]
    [InlineData(DeviceStatus.Synced)]
    [InlineData(DeviceStatus.Added)]
    [InlineData(DeviceStatus.Failed)]
    public async Task Row12_CorpIDNotFound_DB412_FreshSynced_TagDisabled_NoCounterClearsDevice(DeviceStatus freshStatus)
    {
        // Arrange
        var device = MakeDevice();
        var freshDevice = new Device
        {
            Id = device.Id,
            Make = device.Make,
            Model = device.Model,
            SerialNumber = device.SerialNumber,
            PartitionKey = device.PartitionKey,
            Tags = [TagId],
            Status = freshStatus,
            CorporateIdentityID = "corp-id-001",
            CorporateIdentity = "identity-hash",
        };

        var db = new ScenarioDbService(device)
        {
            UpdateDeviceException = CosmosPreconditionFailed(),
            FreshDevice = freshDevice,
            NonSyncingTagsOnRecheck = [TagId],
        };
        var graph = new ScenarioGraphService(DeleteCorpIdResult.NotFound);

        // Act
        await RunSection1(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount, db.Counter.CorpIDCount);
        Assert.Equal(2, db.UpdateDeviceCallCount); // first (412) + retry
        Assert.Equal(DeviceStatus.NotSyncing, db.LastUpdatedDevice!.Status);
        Assert.Equal(string.Empty, db.LastUpdatedDevice.CorporateIdentityID);
        Assert.Equal(string.Empty, db.LastUpdatedDevice.CorporateIdentity);
    }
    #endregion

    #region Row13_CorpIDNotFound_DB412_FreshSynced_TagEnabled
    // ====================================================================
    // Row 13: CorpID Present=Yes, Removed=No (NotFound), DB Updated=No (412),
    //          Fresh device is Synced/Added/Failed, Tag re-enabled
    // Expected: CorpIDCount no change, leave device state
    // ====================================================================

    /// <summary>
    /// CorpID not found in Graph; Cosmos 412; fresh device is Synced; tag re-enabled.
    /// Counter unchanged, device left as-is.
    /// </summary>
    [Fact]
    public async Task Row13_CorpIDNotFound_DB412_FreshSynced_TagReEnabled_NoChange()
    {
        // Arrange
        var device = MakeDevice();
        var freshDevice = new Device
        {
            Id = device.Id,
            Make = device.Make,
            Model = device.Model,
            SerialNumber = device.SerialNumber,
            PartitionKey = device.PartitionKey,
            Tags = [TagId],
            Status = DeviceStatus.Synced,
            CorporateIdentityID = "corp-id-001",
            CorporateIdentity = "identity-hash",
        };

        var db = new ScenarioDbService(device)
        {
            UpdateDeviceException = CosmosPreconditionFailed(),
            FreshDevice = freshDevice,
            NonSyncingTagsOnRecheck = [], // tag no longer disabled
        };
        var graph = new ScenarioGraphService(DeleteCorpIdResult.NotFound);

        // Act
        await RunSection1(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount, db.Counter.CorpIDCount);
        Assert.Equal(1, db.UpdateDeviceCallCount);
    }
    #endregion

    #region Row14_CorpIDNotFound_DB412_FreshSynced_TagCheckErrored
    // ====================================================================
    // Row 14: CorpID Present=Yes, Removed=No (NotFound), DB Updated=No (412),
    //          Fresh device is Synced/Added/Failed, Tag re-check throws
    // Expected: CorpIDCount no change, no change to device
    // ====================================================================

    /// <summary>
    /// CorpID not found in Graph; Cosmos 412; fresh device is Synced; tag re-check throws.
    /// Deferred to next run. Counter unchanged, device unchanged.
    /// </summary>
    [Fact]
    public async Task Row14_CorpIDNotFound_DB412_FreshSynced_TagCheckFails_NoChange()
    {
        // Arrange
        var device = MakeDevice();
        var freshDevice = new Device
        {
            Id = device.Id,
            Make = device.Make,
            Model = device.Model,
            SerialNumber = device.SerialNumber,
            PartitionKey = device.PartitionKey,
            Tags = [TagId],
            Status = DeviceStatus.Synced,
            CorporateIdentityID = "corp-id-001",
            CorporateIdentity = "identity-hash",
        };

        var db = new ScenarioDbService(device)
        {
            UpdateDeviceException = CosmosPreconditionFailed(),
            FreshDevice = freshDevice,
            NonSyncingTagsRecheckException = new Exception("Cosmos read failure"),
        };
        var graph = new ScenarioGraphService(DeleteCorpIdResult.NotFound);

        // Act
        await RunSection1(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount, db.Counter.CorpIDCount);
        Assert.Equal(1, db.UpdateDeviceCallCount);
    }
    #endregion

    #region Row15_CorpIDNotFound_DB412_GetDeviceThrows
    // ====================================================================
    // Row 15: CorpID Present=Yes, Removed=No (NotFound), DB Updated=No (412),
    //          GetDevice throws (Errored)
    // Expected: CorpIDCount no change, no change to device
    // ====================================================================

    /// <summary>
    /// CorpID not found in Graph; Cosmos 412; re-read throws.
    /// Deferred to next run. Counter unchanged, device unchanged.
    /// </summary>
    [Fact]
    public async Task Row15_CorpIDNotFound_DB412_GetDeviceThrows_NoChange()
    {
        // Arrange
        var device = MakeDevice();
        var db = new ScenarioDbService(device)
        {
            UpdateDeviceException = CosmosPreconditionFailed(),
            GetDeviceException = new Exception("Cosmos read failure"),
        };
        var graph = new ScenarioGraphService(DeleteCorpIdResult.NotFound);

        // Act
        await RunSection1(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount, db.Counter.CorpIDCount);
        Assert.Equal(1, db.UpdateDeviceCallCount);
    }
    #endregion

    #region Row16_CorpIDNotFound_DBOtherException
    // ====================================================================
    // Row 16: CorpID Present=Yes, Removed=No (NotFound), DB Updated=No (other exception)
    // Expected: CorpIDCount no change, no change to device
    // ====================================================================

    /// <summary>
    /// CorpID not found in Graph; Cosmos update throws a non-404/non-412 exception.
    /// Counter unchanged, device unchanged.
    /// </summary>
    [Fact]
    public async Task Row16_CorpIDNotFound_DBOtherException_NoChange()
    {
        // Arrange
        var device = MakeDevice();
        var db = new ScenarioDbService(device)
        {
            UpdateDeviceException = new InvalidOperationException("Unexpected Cosmos failure"),
        };
        var graph = new ScenarioGraphService(DeleteCorpIdResult.NotFound);

        // Act
        await RunSection1(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount, db.Counter.CorpIDCount);
    }
    #endregion

    #region Row17_CorpIDError_NoProcessing
    // ====================================================================
    // Row 17: CorpID Present=Yes, Removed=No (Error)
    // Expected: CorpIDCount no change, no change to device
    // ====================================================================

    /// <summary>
    /// Graph returns Error when attempting to delete CorpID.
    /// Processing stops for this device. Counter unchanged, device unchanged.
    /// </summary>
    [Fact]
    public async Task Row17_GraphDeleteError_NoCounterChangeNoDeviceUpdate()
    {
        // Arrange
        var device = MakeDevice();
        var db = new ScenarioDbService(device);
        var graph = new ScenarioGraphService(DeleteCorpIdResult.Error);

        // Act
        await RunSection1(db, graph);

        // Assert
        Assert.Equal(InitialCorpIDCount, db.Counter.CorpIDCount);
        Assert.Equal(0, db.UpdateDeviceCallCount);
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
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
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

    private sealed class ScenarioGraphService : IGraphBetaService
    {
        private readonly DeleteCorpIdResult _deleteResult;
        public ScenarioGraphService(DeleteCorpIdResult deleteResult) => _deleteResult = deleteResult;

        public Task<DeleteCorpIdResult> DeleteCorporateIdentifier(string identifierID)
            => Task.FromResult(_deleteResult);

        public Task<ImportedDeviceIdentity> AddCorporateIdentifier(ImportedDeviceIdentityType type, string identifier)
            => throw new NotImplementedException();
        public Task<bool> CorporateIdentifierExists(string identiferID)
            => throw new NotImplementedException();
        public Task<int> GetCorporateDeviceIdentifierCountAsync()
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Configurable fake for <see cref="ICosmosDbService"/> designed for RemoveSyncedDevicesInDisabledTags scenarios.
    /// Supports differentiated behavior across calls (e.g., first UpdateDevice throws 412, retry succeeds).
    /// </summary>
    private sealed class ScenarioDbService : ICosmosDbService
    {
        private readonly Device _device;
        private int _getNonSyncingCallCount;
        private int _updateDeviceCallCount;

        public ScenarioDbService(Device device)
        {
            _device = device;
            Counter = new CorpIDCounter(0) { CorpIDCount = InitialCorpIDCount };
        }

        // --- Configurable behavior ---

        /// <summary>Exception thrown by the FIRST UpdateDevice call. If null, succeeds.</summary>
        public Exception? UpdateDeviceException { get; set; }

        /// <summary>Device returned by GetDevice (re-read after 412). Null simulates deleted device.</summary>
        public Device? FreshDevice { get; set; }

        /// <summary>Exception thrown by GetDevice. Simulates read failure after 412.</summary>
        public Exception? GetDeviceException { get; set; }

        /// <summary>Tags returned by the SECOND GetNonSyncingDeviceTags call (412 re-check). Defaults to initial list.</summary>
        public List<string>? NonSyncingTagsOnRecheck { get; set; }

        /// <summary>Exception thrown by the second GetNonSyncingDeviceTags call (tag re-check).</summary>
        public Exception? NonSyncingTagsRecheckException { get; set; }

        /// <summary>Counter used by CorpIdCapacityManager.</summary>
        public CorpIDCounter Counter { get; set; }

        // --- Tracking ---

        public int UpdateDeviceCallCount => _updateDeviceCallCount;
        public Device? LastUpdatedDevice { get; private set; }

        // --- ICosmosDbService implementation ---

        public Task<List<string>> GetNonSyncingDeviceTags()
        {
            _getNonSyncingCallCount++;
            if (_getNonSyncingCallCount > 1)
            {
                if (NonSyncingTagsRecheckException is not null)
                    throw NonSyncingTagsRecheckException;
                return Task.FromResult(NonSyncingTagsOnRecheck ?? new List<string> { TagId });
            }
            return Task.FromResult(new List<string> { TagId });
        }

        public Task<List<Device>> GetSyncedDevicesInTags(List<string> tagIds, int batchSize)
            => Task.FromResult(new List<Device> { _device });

        public Task UpdateDevice(Device device)
        {
            _updateDeviceCallCount++;
            LastUpdatedDevice = device;

            if (_updateDeviceCallCount == 1 && UpdateDeviceException is not null)
                throw UpdateDeviceException;

            return Task.CompletedTask;
        }

        public Task<Device?> GetDevice(Guid id, string partitionKey)
        {
            if (GetDeviceException is not null)
                throw GetDeviceException;
            return Task.FromResult(FreshDevice);
        }

        public Task<CorpIDCounter> GetCorpIDCounter()
            => Task.FromResult(Counter);

        public Task<bool> TrySetCorpIDCounter(CorpIDCounter counter, string etag)
        {
            Counter = counter;
            return Task.FromResult(true);
        }

        // Section 2 — configured as no-op (empty syncing tags)
        public Task<List<string>> GetSyncingDeviceTags()
            => Task.FromResult(new List<string>());

        // --- Not used by these tests ---
        public Task<List<Device>> GetAddedDevices(int batchSize) => throw new NotImplementedException();
        public Task<List<Device>> GetAddedDevicesNotSyncing(List<string> tagIds, int batchSize) => throw new NotImplementedException();
        public Task<List<Device>> GetAddedDevicesToSync(List<string> tagIds, int batchSize) => throw new NotImplementedException();
        public Task<List<Device>> GetDevicesMarkedForDeletion() => throw new NotImplementedException();
        public Task DeleteDevice(Device device) => throw new NotImplementedException();
        public Task<List<Device>> GetDevicesSyncedBefore(DateTime date) => throw new NotImplementedException();
        public Task<List<Device>> GetSyncedDevicesSyncedBefore(DateTime date) => throw new NotImplementedException();
        public Task<DeviceTag> GetDeviceTag(string id) => throw new NotImplementedException();
        public Task<List<Device>> GetNotSyncingDevicesInTags(List<string> tagsWithSyncEnabled, int batchSize) => throw new NotImplementedException();
        public Task<int> GetSyncedDeviceCountAsync() => throw new NotImplementedException();
    }
}