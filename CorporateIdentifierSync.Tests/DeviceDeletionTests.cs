using CorporateIdentifierSync.Enums;
using CorporateIdentifierSync.Interfaces;
using CorporateIdentifierSync.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph.Beta.Models;
using Device = DelegationStationShared.Models.Device;
using DeviceTag = DelegationStationShared.Models.DeviceTag;

namespace CorporateIdentifierSync.Tests.DeviceDeletionTests;

[Collection("EnvVarTests")]
public class DeviceDeletionTests
{
    // ====================================================================
    // Helpers
    // ====================================================================

    private static TimerInfo CreateTimerInfo(bool withScheduleStatus = false)
    {
        var timer = new TimerInfo();
        if (withScheduleStatus)
        {
            timer.ScheduleStatus = new ScheduleStatus { Next = DateTime.UtcNow.AddHours(1) };
        }

        return timer;
    }

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

    // ====================================================================
    // GetEnvironmentVariables tests
    // ====================================================================

    /// <summary>
    /// Verifies that GetEnvironmentVariables does not throw when MAX_CORPIDS_ALLOWED is not set.
    /// </summary>
    [Fact]
    public void GetEnvironmentVariables_WhenEnvVarNotSet_DoesNotThrow()
    {
        // Arrange
        Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", null);
        var sut = CreateSut();

        // Act & Assert
        sut.GetEnvironmentVariables();
    }

    /// <summary>
    /// Verifies that GetEnvironmentVariables does not throw when MAX_CORPIDS_ALLOWED is set to a non-numeric string.
    /// </summary>
    [Fact]
    public void GetEnvironmentVariables_WhenEnvVarIsInvalidString_DoesNotThrow()
    {
        // Arrange
        Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", "not-a-number");
        var sut = CreateSut();

        try
        {
            // Act & Assert
            sut.GetEnvironmentVariables();
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", null);
        }
    }

    /// <summary>
    /// Verifies that GetEnvironmentVariables does not throw when MAX_CORPIDS_ALLOWED is set to zero.
    /// </summary>
    [Fact]
    public void GetEnvironmentVariables_WhenEnvVarIsZero_DoesNotThrow()
    {
        // Arrange
        Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", "0");
        var sut = CreateSut();

        try
        {
            // Act & Assert
            sut.GetEnvironmentVariables();
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", null);
        }
    }

    /// <summary>
    /// Verifies that GetEnvironmentVariables does not throw when MAX_CORPIDS_ALLOWED is set to a negative value.
    /// </summary>
    [Fact]
    public void GetEnvironmentVariables_WhenEnvVarIsNegative_DoesNotThrow()
    {
        // Arrange
        Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", "-100");
        var sut = CreateSut();

        try
        {
            // Act & Assert
            sut.GetEnvironmentVariables();
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", null);
        }
    }

    /// <summary>
    /// Verifies that GetEnvironmentVariables does not throw when MAX_CORPIDS_ALLOWED is set to a valid positive number.
    /// </summary>
    [Fact]
    public void GetEnvironmentVariables_WhenEnvVarIsValidPositiveNumber_DoesNotThrow()
    {
        // Arrange
        Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", "5000");
        var sut = CreateSut();

        try
        {
            // Act & Assert
            sut.GetEnvironmentVariables();
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", null);
        }
    }

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

    // ====================================================================
    // Run – ScheduleStatus branch
    // ====================================================================

    /// <summary>
    /// Verifies that Run completes successfully when the timer has no schedule status.
    /// </summary>
    [Fact]
    public async Task Run_WhenScheduleStatusIsNull_CompletesSuccessfully()
    {
        // Arrange
        var dbService = new FakeDbService();
        var sut = CreateSut(dbService: dbService);
        var timer = CreateTimerInfo(withScheduleStatus: false);

        // Act
        await sut.Run(timer);

        // Assert – function ran as far as querying the DB
        Assert.Equal(1, dbService.GetDevicesCallCount);
    }

    /// <summary>
    /// Verifies that Run completes successfully when the timer has a schedule status.
    /// </summary>
    [Fact]
    public async Task Run_WhenScheduleStatusIsNotNull_CompletesSuccessfully()
    {
        // Arrange
        var dbService = new FakeDbService();
        var sut = CreateSut(dbService: dbService);
        var timer = CreateTimerInfo(withScheduleStatus: true);

        // Act
        await sut.Run(timer);

        // Assert – function ran as far as querying the DB
        Assert.Equal(1, dbService.GetDevicesCallCount);
    }

    // ====================================================================
    // Run – database retrieval failure
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

    // ====================================================================
    // Run – empty device list
    // ====================================================================

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

    // ====================================================================
    // Run – Corporate Identity branching
    // ====================================================================

    /// <summary>
    /// Verifies that Run deletes the device from the database when it has no CorporateIdentityID.
    /// </summary>
    [Fact]
    public async Task Run_WhenDeviceHasNoCorporateIdentityID_DeletesDeviceFromDatabase()
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
    }

    /// <summary>
    /// Verifies that Run deletes the device from the database when the corporate identifier deletion succeeds.
    /// </summary>
    [Fact]
    public async Task Run_WhenDeleteCorporateIdentifierReturnsSuccess_DeletesDeviceFromDatabase()
    {
        // Arrange
        var device = new Device { Make = "Dell", Model = "XPS", SerialNumber = "SN001", CorporateIdentityID = "corp-id-001", CorporateIdentity = "corp-identity" };
        var dbService = new FakeDbService { DevicesToReturn = new List<Device> { device } };
        var graphService = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.Success };
        var sut = CreateSut(dbService: dbService, graphBetaService: graphService);
        var timer = CreateTimerInfo();

        // Act
        await sut.Run(timer);

        // Assert
        Assert.Equal(1, dbService.DeleteDeviceCallCount);
    }

    /// <summary>
    /// Verifies that Run still deletes the device from the database even when the Graph API returns NotFound for the corporate identifier.
    /// </summary>
    [Fact]
    public async Task Run_WhenDeleteCorporateIdentifierReturnsNotFound_StillDeletesDeviceFromDatabase()
    {
        // Arrange
        var device = new Device { Make = "HP", Model = "Elite", SerialNumber = "SN002", CorporateIdentityID = "corp-id-002" };
        var dbService = new FakeDbService { DevicesToReturn = new List<Device> { device } };
        var graphService = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.NotFound };
        var sut = CreateSut(dbService: dbService, graphBetaService: graphService);
        var timer = CreateTimerInfo();

        // Act
        await sut.Run(timer);

        // Assert – NotFound means already gone from Graph; Cosmos deletion still proceeds
        Assert.Equal(1, dbService.DeleteDeviceCallCount);
    }

    /// <summary>
    /// Verifies that Run does not delete the device from the database when the corporate identifier deletion returns an error.
    /// </summary>
    [Fact]
    public async Task Run_WhenDeleteCorporateIdentifierReturnsError_DoesNotDeleteDeviceFromDatabase()
    {
        // Arrange
        var device = new Device { Make = "Lenovo", Model = "ThinkPad", SerialNumber = "SN003", CorporateIdentityID = "corp-id-003" };
        var dbService = new FakeDbService { DevicesToReturn = new List<Device> { device } };
        var graphService = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.Error };
        var sut = CreateSut(dbService: dbService, graphBetaService: graphService);
        var timer = CreateTimerInfo();

        // Act
        await sut.Run(timer);

        // Assert – corp-id deletion failed; Cosmos deletion must be skipped
        Assert.Equal(0, dbService.DeleteDeviceCallCount);
    }

    // ====================================================================
    // Run – Cosmos deletion exception handling
    // ====================================================================

    /// <summary>
    /// Verifies that Run treats a Cosmos NotFound exception during device deletion as a success and continues processing.
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
    }

    /// <summary>
    /// Verifies that Run logs a generic exception thrown during device deletion and continues processing subsequent devices.
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
    }

    // ====================================================================
    // Run – multiple devices
    // ====================================================================

    /// <summary>
    /// Verifies that Run processes all devices when multiple devices are marked for deletion.
    /// </summary>
    [Fact]
    public async Task Run_WithMultipleDevices_ProcessesAllDevices()
    {
        // Arrange
        var devices = new List<Device>
        {
            new Device { Make = "Dell", Model = "XPS", SerialNumber = "SN001", CorporateIdentityID = string.Empty },
            new Device { Make = "HP", Model = "Elite", SerialNumber = "SN002", CorporateIdentityID = string.Empty },
            new Device { Make = "Lenovo", Model = "ThinkPad", SerialNumber = "SN003", CorporateIdentityID = string.Empty },
        };
        var dbService = new FakeDbService { DevicesToReturn = devices };
        var sut = CreateSut(dbService: dbService);
        var timer = CreateTimerInfo();

        // Act
        await sut.Run(timer);

        // Assert
        Assert.Equal(3, dbService.DeleteDeviceCallCount);
    }

    // ====================================================================
    // Run – CorpID capacity release
    // ====================================================================

    /// <summary>
    /// Verifies that Run releases capacity in the CorpID counter after successfully deleting corporate identifiers.
    /// </summary>
    [Fact]
    public async Task Run_WhenCorpIDsAreDeleted_ReleasesCapacityInCounter()
    {
        // Arrange
        var device = new Device
        {
            Make = "Dell", Model = "XPS", SerialNumber = "SN001",
            CorporateIdentityID = "corp-id-001", CorporateIdentity = "identity",
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

        // Assert – counter should have been updated (decremented) after the release
        Assert.True(dbService.TrySetCorpIDCounterCallCount > 0);
        Assert.Equal(4, dbService.Counter.CorpIDCount);
    }

    /// <summary>
    /// Verifies that Run does not propagate an exception when releasing CorpID capacity fails.
    /// </summary>
    [Fact]
    public async Task Run_WhenReleaseCorpIDsThrows_DoesNotPropagateException()
    {
        // Arrange
        var device = new Device
        {
            Make = "Dell", Model = "XPS", SerialNumber = "SN001",
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

    /// <summary>
    /// Verifies that Run does not attempt to release capacity when no corporate identifiers were successfully deleted.
    /// </summary>
    [Fact]
    public async Task Run_WhenNoCorpIDsWereDeleted_DoesNotAttemptCapacityRelease()
    {
        // Arrange – devices have corp IDs but graph deletion returns Error (so corpIDsDeletedCount stays 0)
        var device = new Device
        {
            Make = "Dell", Model = "XPS", SerialNumber = "SN001",
            CorporateIdentityID = "corp-id-001",
        };
        var dbService = new FakeDbService { DevicesToReturn = new List<Device> { device } };
        var graphService = new FakeGraphBetaService { DeleteResult = DeleteCorpIdResult.Error };
        var sut = CreateSut(dbService: dbService, graphBetaService: graphService);
        var timer = CreateTimerInfo();

        // Act
        await sut.Run(timer);

        // Assert – no capacity release attempted
        Assert.Equal(0, dbService.TrySetCorpIDCounterCallCount);
    }

    // ====================================================================
    // Run – env var integration through Run
    // ====================================================================

    /// <summary>
    /// Verifies that Run uses a custom capacity cap from the MAX_CORPIDS_ALLOWED environment variable during capacity release.
    /// </summary>
    [Fact]
    public async Task Run_WhenMaxCorpIDsAllowedEnvVarIsSet_UsesCustomCapInCapacityRelease()
    {
        // Arrange – setting a large cap so all deletions are within capacity
        Environment.SetEnvironmentVariable("MAX_CORPIDS_ALLOWED", "99999");
        var device = new Device
        {
            Make = "Dell", Model = "XPS", SerialNumber = "SN001",
            CorporateIdentityID = "corp-id-001", CorporateIdentity = "identity",
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
    }
}
