using CorporateIdentifierSync.Enums;
using CorporateIdentifierSync.Interfaces;
using DelegationStationShared.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph.Beta.Models;
using System.Reflection;
using Device = DelegationStationShared.Models.Device;
using DeviceTag = DelegationStationShared.Models.DeviceTag;

namespace CorporateIdentifierSync.Tests.DeviceDeletionTests;

[Collection("EnvVarTests")]
public class DeviceDeletionTests
{
    #region helpers
    // ====================================================================
    // Helpers
    // ====================================================================

    private static TimerInfo CreateTimerInfo() => new TimerInfo();

    private static DeviceDeletion CreateSut(
        ILogger<DeviceDeletion>? logger = null,
        ICosmosDbService? dbService = null,
        IGraphBetaService? graphBetaService = null,
        IFunctionSingletonLock? singletonLock = null)
    {
        return new DeviceDeletion(
            logger ?? NullLogger<DeviceDeletion>.Instance,
            dbService ?? new FakeDbService(),
            graphBetaService ?? new FakeGraphBetaService(),
            singletonLock ?? new FakeSingletonLock(new FakeAsyncDisposable()));
    }

    private static CosmosException CreateCosmosException(System.Net.HttpStatusCode statusCode)
        => new CosmosException("Simulated Cosmos error", statusCode, 0, Guid.NewGuid().ToString(), 0);

    private static int GetMaxCorpIDsAllowed(DeviceDeletion sut)
    {
        var field = typeof(DeviceDeletion)
            .GetField("_MaxCorpIDsAllowed", BindingFlags.NonPublic | BindingFlags.Instance);
        return (int)field!.GetValue(sut)!;
    }
    #endregion helpers

    #region ConstructorTests
    // ====================================================================
    // Constructor tests
    // ====================================================================

    /// <summary>
    /// Verifies that the constructor creates a valid DeviceDeletion instance when all dependencies are provided.
    /// </summary>
    [Fact]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
        // Arrange & Act
        var sut = new DeviceDeletion(
            NullLogger<DeviceDeletion>.Instance,
            new FakeDbService(),
            new FakeGraphBetaService(),
            new FakeSingletonLock(new FakeAsyncDisposable()));

        // Assert
        Assert.NotNull(sut);
    }
    #endregion ConstructorTests

    #region GetEnvironmentVariableTests
    // ====================================================================
    // GetEnvironmentVariables tests
    // ====================================================================

    /// <summary>
    /// Verifies that GetEnvironmentVariables sets _MaxCorpIDsAllowed to the expected value.
    /// Invalid, missing, zero, and negative inputs fall back to the default of 10000;
    /// a valid positive value is used directly.
    /// </summary>
    [Theory]
    [InlineData(null, 10000)]           // not set → default
    [InlineData("not-a-number", 10000)] // unparseable → default
    [InlineData("0", 10000)]            // zero (must be > 0) → default
    [InlineData("-100", 10000)]         // negative → default
    [InlineData("5000", 5000)]          // valid positive → used as-is
    public void GetEnvironmentVariables_SetsMaxCorpIDsAllowedFromEnvVar(string? envVarValue, int expectedMax)
    {
        // Arrange
        Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", envVarValue);
        var sut = CreateSut();

        try
        {
            // Act
            sut.GetEnvironmentVariables();

            // Assert
            Assert.Equal(expectedMax, GetMaxCorpIDsAllowed(sut));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", null);
        }
    }
    #endregion GetEnvironmentVariableTests

    #region SingletonLockTests
    // ====================================================================
    // Run – singleton lock tests
    // ====================================================================

    /// <summary>
    /// Verifies that Run exits early without querying the database when the singleton lock cannot be acquired.
    /// </summary>
    [Fact]
    public async Task Run_WhenSingletonLockNotAcquired_ExitsWithoutQueryingDatabase()
    {
        // Arrange
        var dbService = new FakeDbService();
        var sut = CreateSut(dbService: dbService, singletonLock: new FakeSingletonLock(handle: null));
        var timer = CreateTimerInfo();

        // Act
        await sut.Run(timer);

        // Assert
        Assert.Equal(0, dbService.GetDevicesCallCount);
    }
    #endregion SingletonLockTests


    #region EarlyExitTests
    // ====================================================================
    // Early Exits
    // ====================================================================

    /// <summary>
    /// Verifies that Run exits without deleting any devices when GetDevicesMarkedForDeletion throws an exception.
    /// </summary>
    [Fact]
    public async Task Run_WhenGetDevicesMarkedForDeletionThrows_ExitsWithoutDeletingDevices()
    {
        // Arrange
        var dbService = new FakeDbService { GetDevicesException = new Exception("DB connection failure") };
        var sut = CreateSut(dbService: dbService);
        var timer = CreateTimerInfo();

        // Act
        await sut.Run(timer);

        // Assert
        Assert.Equal(1, dbService.GetDevicesCallCount);
        Assert.Equal(0, dbService.DeleteDeviceCallCount);
    }

    /// <summary>
    /// Verifies that Run exits without performing any deletions when no devices are marked for deletion.
    /// </summary>
    [Fact]
    public async Task Run_WhenNoDevicesMarkedForDeletion_ExitsWithoutDeletingAnything()
    {
        // Arrange
        var dbService = new FakeDbService { DevicesToReturn = new List<Device>() };
        var sut = CreateSut(dbService: dbService);
        var timer = CreateTimerInfo();

        // Act
        await sut.Run(timer);

        // Assert
        Assert.Equal(0, dbService.DeleteDeviceCallCount);
    }
    #endregion EarlyExitTests

    #region HappyPathTests
    // ====================================================================
    // Happy Path Tests
    // ====================================================================

    /// <summary>
    /// Deleting device without CorpID in DB
    /// Expected behavior:  Deleted from DB, no CorpID release
    /// </summary>
    [Fact]
    public async Task Run_WhenNoCorpID_AndDeviceDeletionSucceeds_CounterUnchanged()
    {
        // Arrange
        var device = new Device { Make = "Dell", Model = "XPS", SerialNumber = "SN001", CorporateIdentityID = string.Empty };
        var dbService = new FakeDbService { DevicesToReturn = new List<Device> { device } };
        var sut = CreateSut(dbService: dbService);
        var timer = CreateTimerInfo();

        // Act
        await sut.Run(timer);

        // Assert
        Assert.Equal(1, dbService.DeleteDeviceCallCount);
        Assert.Equal(0, dbService.TrySetCorpIDCounterCallCount);
    }

    /// <summary>
    /// Deleting device with CorpID in DB
    /// Expected behavior:  Deleted from CorpID, deleted from DB, CorpID counter decremented by 1
    /// </summary>
    [Fact]
    public async Task Run_WhenCorpIDPresentAndGraphSucceeds_DeletesDeviceAndDecrementsCounter()
    {
        // Arrange
        var device = new Device
        {
            Make = "Dell",
            Model = "XPS",
            SerialNumber = "SN001",
            CorporateIdentityID = "corp-id-001",
            CorporateIdentity = "identity",
        };
        var dbService = new FakeDbService
        {
            DevicesToReturn = new List<Device> { device },
            Counter = new CorpIDCounter(0) { CorpIDCount = 5 },
        };
        var graphService = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.Success };
        var sut = CreateSut(dbService: dbService, graphBetaService: graphService);
        var timer = CreateTimerInfo();

        // Act
        await sut.Run(timer);

        // Assert
        Assert.Equal(1, dbService.DeleteDeviceCallCount);
        Assert.Equal(4, dbService.Counter.CorpIDCount);
    }
    #endregion HappyPathTests

    #region CosmosErrorHandlingTests
    // ====================================================================
    // Error Handling - Cosmos Errors
    // ====================================================================

    /// <summary>
    /// Device without CorpID, Cosmos delete throws 404 NotFound
    /// Expected behavior: Device deletion treated as successful, no CorpID counter update
    /// </summary>
    [Fact]
    public async Task Run_WhenDeleteDeviceThrowsCosmosNotFound_TreatsAsSuccessAndContinues()
    {
        // Arrange
        var device = new Device { Make = "Dell", Model = "XPS", SerialNumber = "SN001", CorporateIdentityID = string.Empty };
        var dbService = new FakeDbService
        {
            DevicesToReturn = new List<Device> { device },
            DeleteDeviceException = CreateCosmosException(System.Net.HttpStatusCode.NotFound),
        };
        var sut = CreateSut(dbService: dbService);
        var timer = CreateTimerInfo();

        // Act – should not throw
        await sut.Run(timer);

        // Assert
        Assert.Equal(1, dbService.DeleteDeviceCallCount);
        Assert.Equal(0, dbService.TrySetCorpIDCounterCallCount); // Row 2: no corp ID → counter must not change
    }

    /// <summary>
    /// Device with no CorpID, Cosmos delete throws exception other than 404
    /// Expected behavior:  Stops processing this device, no update to CorpID (will retry on next run)
    /// </summary>
    [Fact]
    public async Task Run_WhenDeleteDeviceThrowsGenericException_LogsAndContinues()
    {
        // Arrange
        var device = new Device { Make = "Dell", Model = "XPS", SerialNumber = "SN001", CorporateIdentityID = string.Empty };
        var dbService = new FakeDbService
        {
            DevicesToReturn = new List<Device> { device },
            DeleteDeviceException = new InvalidOperationException("Generic Cosmos error"),
        };
        var sut = CreateSut(dbService: dbService);
        var timer = CreateTimerInfo();

        // Act – exception is caught and logged; must not propagate
        await sut.Run(timer);

        // Assert
        Assert.Equal(1, dbService.DeleteDeviceCallCount);
        Assert.Equal(0, dbService.TrySetCorpIDCounterCallCount);
    }

    /// <summary>
    /// Device with CorpID in DB, CorpID deleted from Graph successfully; Cosmos device deletion returns 404.
    /// Expected Behavior:  Cosmos deletion treated as successful, CorpID counter decremented by 1
    /// </summary>
    [Fact]
    public async Task Run_WhenCorpIDDeletedFromGraph_AndCosmosDeviceDeletion404_StillDecrementsCounter()
    {
        // Arrange
        var device = new Device
        {
            Make = "Dell",
            Model = "XPS",
            SerialNumber = "SN001",
            CorporateIdentityID = "corp-id-001",
            CorporateIdentity = "identity",
        };
        var dbService = new FakeDbService
        {
            DevicesToReturn = new List<Device> { device },
            DeleteDeviceException = CreateCosmosException(System.Net.HttpStatusCode.NotFound),
            Counter = new CorpIDCounter(0) { CorpIDCount = 5 },
        };
        var graphService = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.Success };
        var sut = CreateSut(dbService: dbService, graphBetaService: graphService);
        var timer = CreateTimerInfo();

        // Act
        await sut.Run(timer);

        // Assert – 404 treated as success; counter decremented because Graph deletion succeeded
        Assert.Equal(1, dbService.DeleteDeviceCallCount);
        Assert.Equal(4, dbService.Counter.CorpIDCount);
    }
    /// <summary>
    /// Device has CorpID, CorpID successfuly deleted, but Comsos delete throws generic exception
    /// Expected behavior:  Decrement CorpID counter, DB object deletion should be tried on next run
    /// </summary>
    [Fact]
    public async Task Run_WhenCorpIDDeletedFromGraph_AndCosmosDeviceDeletionThrows_StillDecrementsCounter()
    {
        // Arrange
        var device = new Device
        {
            Make = "Dell",
            Model = "XPS",
            SerialNumber = "SN001",
            CorporateIdentityID = "corp-id-001",
        };
        var dbService = new FakeDbService
        {
            DevicesToReturn = new List<Device> { device },
            DeleteDeviceException = new InvalidOperationException("Cosmos write failure"),
            Counter = new CorpIDCounter(0) { CorpIDCount = 5 },
        };
        var graphService = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.Success };
        var sut = CreateSut(dbService: dbService, graphBetaService: graphService);
        var timer = CreateTimerInfo();

        // Act
        await sut.Run(timer);

        // Assert – Cosmos deletion failed but corp ID was already removed; counter still decremented
        Assert.Equal(1, dbService.DeleteDeviceCallCount);
        Assert.Equal(4, dbService.Counter.CorpIDCount);
    }
    #endregion CosmosErrorHandlingTests

    #region GraphErrorHandlingTest
    // ====================================================================
    // Error handling - CorpID deletion failures
    // ====================================================================

    /// <summary>
    /// CorpID present in DB but not found in Graph
    /// Expected behavior:  Attempts DB deletion and doesn't update CorpID Counter
    /// </summary>
    [Fact]
    public async Task Run_WhenCorpIDNotFoundInGraph_AndDeviceDeletionSucceeds_CounterUnchanged()
    {
        // Arrange
        var device = new Device { Make = "Dell", Model = "XPS", SerialNumber = "SN001", CorporateIdentityID = "corp-id-001" };
        var dbService = new FakeDbService { DevicesToReturn = new List<Device> { device } };
        var graphService = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.NotFound };
        var sut = CreateSut(dbService: dbService, graphBetaService: graphService);
        var timer = CreateTimerInfo();

        // Act
        await sut.Run(timer);

        // Assert
        Assert.Equal(1, dbService.DeleteDeviceCallCount);
        Assert.Equal(0, dbService.TrySetCorpIDCounterCallCount);
    }

    /// <summary>
    /// Device has CorpID in DB, Graph deletion fails with generic exception
    /// Expected behavior:  Does not attempt DB deletion, does not update CorpID Counter (will retry on next run)
    /// </summary>
    [Fact]
    public async Task Run_WhenCorpIDPresentAndGraphFails_SkipsDeviceDeletionAndLeavesCounterUnchanged()
    {
        // Arrange
        var device = new Device { Make = "Dell", Model = "XPS", SerialNumber = "SN001", CorporateIdentityID = "corp-id-001" };
        var dbService = new FakeDbService { DevicesToReturn = new List<Device> { device } };
        var graphService = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.Error };
        var sut = CreateSut(dbService: dbService, graphBetaService: graphService);
        var timer = CreateTimerInfo();

        // Act
        await sut.Run(timer);

        // Assert
        Assert.Equal(0, dbService.DeleteDeviceCallCount);
        Assert.Equal(0, dbService.TrySetCorpIDCounterCallCount);
    }
    #endregion GraphErrorHandlingTest

    #region CosmosAndGraphErrorHandlingTests
    // ====================================================================
    // Error handling - CorpID deletion failure and Cosmos DB failure combinations
    // ====================================================================

    /// <summary>
    /// Device with CorpID in DB, CorpID not found and Cosmos delete throws 404
    /// Expected Behavior:  CorpID and DB object don't exist, counter is not updated
    /// </summary>
    [Fact]
    public async Task Run_WhenCorpIDNotFoundInGraph_AndCosmosDeviceDeletion404_CounterUnchanged()
    {
        // Arrange
        var device = new Device
        {
            Make = "Dell",
            Model = "XPS",
            SerialNumber = "SN001",
            CorporateIdentityID = "corp-id-001",
        };
        var dbService = new FakeDbService
        {
            DevicesToReturn = new List<Device> { device },
            DeleteDeviceException = CreateCosmosException(System.Net.HttpStatusCode.NotFound),
            Counter = new CorpIDCounter(0) { CorpIDCount = 5 },
        };
        var graphService = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.NotFound };
        var sut = CreateSut(dbService: dbService, graphBetaService: graphService);
        var timer = CreateTimerInfo();

        // Act
        await sut.Run(timer);

        // Assert – 404 treated as success; counter unchanged because Graph returned NotFound
        Assert.Equal(1, dbService.DeleteDeviceCallCount);
        Assert.Equal(0, dbService.TrySetCorpIDCounterCallCount);
    }

    /// <summary>
    /// Device has CorpID in DB, CorpID not found during deletion attempt, Cosmos delete throws generic exception
    /// Expected behavior:  No counter update since Graph deletion failed; DB object deletion should be tried on next run
    /// </summary>
    [Fact]
    public async Task Run_WhenCorpIDNotFoundInGraph_AndCosmosDeviceDeletionThrows_CounterUnchanged()
    {
        // Arrange
        var device = new Device
        {
            Make = "Dell",
            Model = "XPS",
            SerialNumber = "SN001",
            CorporateIdentityID = "corp-id-001",
        };
        var dbService = new FakeDbService
        {
            DevicesToReturn = new List<Device> { device },
            DeleteDeviceException = new InvalidOperationException("Cosmos write failure"),
            Counter = new CorpIDCounter(0) { CorpIDCount = 5 },
        };
        var graphService = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.NotFound };
        var sut = CreateSut(dbService: dbService, graphBetaService: graphService);
        var timer = CreateTimerInfo();

        // Act
        await sut.Run(timer);

        // Assert – device deletion failed; counter unchanged because Graph returned NotFound
        Assert.Equal(1, dbService.DeleteDeviceCallCount);
        Assert.Equal(0, dbService.TrySetCorpIDCounterCallCount);
    }
    #endregion CosmosAndGraphErrorHandlingTests


    // ====================================================================
    // Run – CorpID capacity release
    // ====================================================================

    /// <summary>
    /// Verifies that Run does not propagate an exception when releasing CorpID capacity fails.
    /// </summary>
    [Fact]
    public async Task Run_WhenReleaseCorpIDsThrows_DoesNotPropagateException()
    {
        // Arrange
        var device = new Device
        {
            Make = "Dell",
            Model = "XPS",
            SerialNumber = "SN001",
            CorporateIdentityID = "corp-id-001",
        };
        var dbService = new FakeDbService
        {
            DevicesToReturn = new List<Device> { device },
            GetCorpIDCounterException = new Exception("Counter unavailable"),
        };
        var graphService = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.Success };
        var sut = CreateSut(dbService: dbService, graphBetaService: graphService);
        var timer = CreateTimerInfo();

        // Act – exception from ReleaseCorpIDs is caught and logged; must not propagate
        await sut.Run(timer);
    }

    // ====================================================================
    // Run – env var integration through Run
    // ====================================================================

    /// <summary>
    /// Verifies that Run uses a MAX_CORPIDS_ALLOWED environment variable during capacity release.
    /// </summary>
    [Fact]
    public async Task Run_WhenMaxCorpIDsAllowedEnvVarIsSet_UsesCustomCapInCapacityRelease()
    {
        // Arrange – setting a large cap so all deletions are within capacity
        Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", "99999");
        var device = new Device
        {
            Make = "Dell",
            Model = "XPS",
            SerialNumber = "SN001",
            CorporateIdentityID = "corp-id-001",
            CorporateIdentity = "identity",
        };
        var dbService = new FakeDbService
        {
            DevicesToReturn = new List<Device> { device },
            Counter = new CorpIDCounter(0) { CorpIDCount = 100 },
        };
        var graphService = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.Success };
        var sut = CreateSut(dbService: dbService, graphBetaService: graphService);
        var timer = CreateTimerInfo();

        try
        {
            // Act
            await sut.Run(timer);

            // Assert – counter decremented from 100 to 99
            Assert.Equal(99, dbService.Counter.CorpIDCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", null);
        }
    }


    // ====================================================================
    // Inner fakes
    // ====================================================================

    private sealed class FakeAsyncDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSingletonLock : IFunctionSingletonLock
    {
        private readonly IAsyncDisposable? _handle;

        public FakeSingletonLock(IAsyncDisposable? handle) => _handle = handle;

        public Task<IAsyncDisposable?> TryAcquireAsync(string lockName, CancellationToken cancellationToken = default)
            => Task.FromResult(_handle);
    }

    private sealed class FakeGraphBetaService : IGraphBetaService
    {
        public DeleteCorpIdResult DeleteResult { get; set; } = DeleteCorpIdResult.Success;

        public Task<DeleteCorpIdResult> DeleteCorporateIdentifier(string identifierID)
            => Task.FromResult(DeleteResult);

        public Task<ImportedDeviceIdentity> AddCorporateIdentifier(ImportedDeviceIdentityType type, string identifier)
            => throw new NotImplementedException();

        public Task<bool> CorporateIdentifierExists(string identiferID)
            => throw new NotImplementedException();

        public Task<int> GetCorporateDeviceIdentifierCountAsync()
            => throw new NotImplementedException();
    }

    private sealed class FakeDbService : ICosmosDbService
    {
        public List<Device> DevicesToReturn { get; set; } = new List<Device>();
        public Exception? GetDevicesException { get; set; }
        public Exception? DeleteDeviceException { get; set; }
        public Exception? GetCorpIDCounterException { get; set; }
        public CorpIDCounter Counter { get; set; } = new CorpIDCounter(0);

        public int GetDevicesCallCount { get; private set; }
        public int DeleteDeviceCallCount { get; private set; }
        public int TrySetCorpIDCounterCallCount { get; private set; }

        public Task<List<Device>> GetDevicesMarkedForDeletion()
        {
            GetDevicesCallCount++;
            if (GetDevicesException is not null)
            {
                throw GetDevicesException;
            }

            return Task.FromResult(DevicesToReturn);
        }

        public Task DeleteDevice(Device device)
        {
            DeleteDeviceCallCount++;
            if (DeleteDeviceException is not null)
            {
                throw DeleteDeviceException;
            }

            return Task.CompletedTask;
        }

        public Task<CorpIDCounter> GetCorpIDCounter()
        {
            if (GetCorpIDCounterException is not null)
            {
                throw GetCorpIDCounterException;
            }

            return Task.FromResult(Counter);
        }

        public Task<bool> TrySetCorpIDCounter(CorpIDCounter counter, string etag)
        {
            TrySetCorpIDCounterCallCount++;
            Counter = counter;
            return Task.FromResult(true);
        }

        // Not used by DeviceDeletion
        public Task<List<Device>> GetAddedDevices(int batchSize) => throw new NotImplementedException();
        public Task<List<Device>> GetAddedDevicesNotSyncing(List<string> tagIds, int batchSize) => throw new NotImplementedException();
        public Task<List<Device>> GetAddedDevicesToSync(List<string> tagIds, int batchSize) => throw new NotImplementedException();
        public Task UpdateDevice(Device device) => throw new NotImplementedException();
        public Task<Device?> GetDevice(Guid id, string partitionKey) => throw new NotImplementedException();
        public Task<List<Device>> GetDevicesSyncedBefore(DateTime date) => throw new NotImplementedException();
        public Task<List<Device>> GetSyncedDevicesSyncedBefore(DateTime date) => throw new NotImplementedException();
        public Task<DeviceTag> GetDeviceTag(string id) => throw new NotImplementedException();
        public Task<List<string>> GetSyncingDeviceTags() => throw new NotImplementedException();
        public Task<List<string>> GetNonSyncingDeviceTags() => throw new NotImplementedException();
        public Task<List<Device>> GetSyncedDevicesInTags(List<string> tagIds, int batchSize) => throw new NotImplementedException();
        public Task<List<Device>> GetNotSyncingDevicesInTags(List<string> tagsWithSyncEnabled, int batchSize) => throw new NotImplementedException();
        public Task<int> GetSyncedDeviceCountAsync() => throw new NotImplementedException();
    }


}
