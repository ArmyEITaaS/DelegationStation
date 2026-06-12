using CorporateIdentifierSync;
using CorporateIdentifierSync.Interfaces;
using CorporateIdentifierSync.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Device = DelegationStationShared.Models.Device;
using DeviceTag = DelegationStationShared.Models.DeviceTag;

namespace CorporateIdentifierSync.Tests.CorpIdCapacityManagerTests
{
    /// <summary>
    /// Fake ICosmosDbService that stores a single CorpIDCounter in memory.
    /// All methods unused by CorpIdCapacityManager throw NotImplementedException.
    /// </summary>
    internal class FakeCosmosDbService : ICosmosDbService
    {
        public CorpIDCounter Counter { get; set; } = new CorpIDCounter(0);

        public Task<CorpIDCounter> GetCorpIDCounter() => Task.FromResult(Counter);

        public Task<bool> TrySetCorpIDCounter(CorpIDCounter counter, string ETag)
        {
            Counter = counter;
            return Task.FromResult(true);
        }

        public Task<List<Device>> GetAddedDevices(int batchSize) => throw new NotImplementedException();
        public Task<List<Device>> GetAddedDevicesNotSyncing(List<string> tagIDs, int batchsize) => throw new NotImplementedException();
        public Task<List<Device>> GetAddedDevicesToSync(List<string> tagIDs, int batchsize) => throw new NotImplementedException();
        public Task<List<Device>> GetDevicesMarkedForDeletion() => throw new NotImplementedException();
        public Task UpdateDevice(Device device) => throw new NotImplementedException();
        public Task DeleteDevice(Device device) => throw new NotImplementedException();
        public Task<List<Device>> GetDevicesSyncedBefore(DateTime date) => throw new NotImplementedException();
        public Task<List<Device>> GetSyncedDevicesSyncedBefore(DateTime date) => throw new NotImplementedException();
        public Task<List<Device>> GetSyncedDevicesInTags(List<string> tagIDs, int batchsize) => throw new NotImplementedException();
        public Task<List<Device>> GetNotSyncingDevicesInTags(List<string> tagIDs, int batchsize) => throw new NotImplementedException();
        public Task<DeviceTag> GetDeviceTag(string id) => throw new NotImplementedException();
        public Task<List<string>> GetSyncingDeviceTags() => throw new NotImplementedException();
        public Task<List<string>> GetNonSyncingDeviceTags() => throw new NotImplementedException();
        public Task<List<Device>> GetSyncedDevices(int batchSize) => throw new NotImplementedException();
        public Task<List<Device>> GetNotSyncingDevices(int batchSize) => throw new NotImplementedException();
        public Task<Device?> GetDevice(Guid id, string partitionKey) => throw new NotImplementedException();
    }

    public class CorpIdCapacityManagerTests
    {
        private const int TotalCap = 1000;

        private static CorpIdCapacityManager CreateManager(FakeCosmosDbService db)
            => new CorpIdCapacityManager(db, NullLogger.Instance, TotalCap);

        // -------------------------------------------------------------------------
        // GetAvailableCorpIDCount
        // -------------------------------------------------------------------------

        /// <summary>
        /// Verifies that GetAvailableCorpIDCount returns the total configured cap when the counter is empty.
        /// </summary>
        [Fact]
        public async Task GetAvailableCorpIDCount_WhenCounterIsEmpty_ReturnsTotalCap()
        {
            var db = new FakeCosmosDbService();
            var manager = CreateManager(db);

            int available = await manager.GetAvailableCorpIDCount(CancellationToken.None);

            Assert.Equal(TotalCap, available);
        }

        /// <summary>
        /// Verifies that GetAvailableCorpIDCount correctly deducts both the CorpID count and the reserve from the total cap.
        /// </summary>
        [Fact]
        public async Task GetAvailableCorpIDCount_DeductsCorpIDCountAndReserve()
        {
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter(0) { CorpIDCount = 300, CorpIDReserve = 100 } };
            var manager = CreateManager(db);

            int available = await manager.GetAvailableCorpIDCount(CancellationToken.None);

            Assert.Equal(600, available);
        }

        /// <summary>
        /// Verifies that GetAvailableCorpIDCount returns zero when the CorpID count equals the total cap.
        /// </summary>
        [Fact]
        public async Task GetAvailableCorpIDCount_WhenAtCap_ReturnsZero()
        {
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter(0) { CorpIDCount = TotalCap } };
            var manager = CreateManager(db);

            int available = await manager.GetAvailableCorpIDCount(CancellationToken.None);

            Assert.Equal(0, available);
        }

        // -------------------------------------------------------------------------
        // ReserveCorpIDs
        // -------------------------------------------------------------------------

        /// <summary>
        /// Verifies that ReserveCorpIDs reserves the full requested count when sufficient capacity is available.
        /// </summary>
        [Fact]
        public async Task ReserveCorpIDs_WhenSufficientCapacity_ReservesRequestedCount()
        {
            var db = new FakeCosmosDbService();
            var manager = CreateManager(db);

            int reserved = await manager.ReserveCorpIDs(50, CancellationToken.None);

            Assert.Equal(50, reserved);
            Assert.Equal(50, db.Counter.CorpIDReserve);
        }

        /// <summary>
        /// Verifies that ReserveCorpIDs returns zero and leaves the counter unchanged when at full capacity.
        /// </summary>
        [Fact]
        public async Task ReserveCorpIDs_WhenAtCap_ReturnsZeroAndDoesNotModifyCounter()
        {
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter(0) { CorpIDCount = TotalCap } };
            var manager = CreateManager(db);

            int reserved = await manager.ReserveCorpIDs(10, CancellationToken.None);

            Assert.Equal(0, reserved);
            Assert.Equal(0, db.Counter.CorpIDReserve);
        }

        /// <summary>
        /// Verifies that ReserveCorpIDs reserves only the remaining available slots when partial capacity remains.
        /// </summary>
        [Fact]
        public async Task ReserveCorpIDs_WhenPartialCapacity_ReservesOnlyAvailableSlots()
        {
            // 20 slots available
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter(0) { CorpIDCount = 980 } };
            var manager = CreateManager(db);

            int reserved = await manager.ReserveCorpIDs(50, CancellationToken.None);

            Assert.Equal(20, reserved);
            Assert.Equal(20, db.Counter.CorpIDReserve);
        }

        /// <summary>
        /// Verifies that ReserveCorpIDs persists the updated reserve value to the database.
        /// </summary>
        [Fact]
        public async Task ReserveCorpIDs_PersistsReserveToDatabase()
        {
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter(0) { CorpIDReserve = 500 } };
            var manager = CreateManager(db);

            await manager.ReserveCorpIDs(25, CancellationToken.None);

            Assert.Equal(525, db.Counter.CorpIDReserve);
        }

        // -------------------------------------------------------------------------
        // CommitCorpIDCount
        // -------------------------------------------------------------------------

        /// <summary>
        /// Verifies that CommitCorpIDCount decrements the reserve and increments the CorpID count by the committed amount.
        /// </summary>
        [Fact]
        public async Task CommitCorpIDCount_DecrementsReserveAndIncrementsCount()
        {
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter(0) { CorpIDReserve = 50 } };
            var manager = CreateManager(db);

            await manager.CommitCorpIDCount(50, 40, CancellationToken.None);

            Assert.Equal(0, db.Counter.CorpIDReserve);
            Assert.Equal(40, db.Counter.CorpIDCount);
        }

        /// <summary>
        /// Verifies that CommitCorpIDCount returns the correct remaining available slot count.
        /// </summary>
        [Fact]
        public async Task CommitCorpIDCount_ReturnsRemainingAvailable()
        {
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter(0) { CorpIDReserve = 100 } };
            var manager = CreateManager(db);

            int available = await manager.CommitCorpIDCount(100, 80, CancellationToken.None);

            Assert.Equal(TotalCap - 80, available);
        }

        /// <summary>
        /// Verifies that CommitCorpIDCount prevents the reserve from going below zero in a drift scenario.
        /// </summary>
        [Fact]
        public async Task CommitCorpIDCount_ReserveDoesNotGoBelowZero_OnDrift()
        {
            // reserved > counter.CorpIDReserve (drift scenario)
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter(0) { CorpIDReserve = 10 } };
            var manager = CreateManager(db);

            await manager.CommitCorpIDCount(50, 10, CancellationToken.None);

            Assert.Equal(0, db.Counter.CorpIDReserve);
        }

        /// <summary>
        /// Verifies that CommitCorpIDCount releases unused reserved slots when fewer devices were synced than reserved.
        /// </summary>
        [Fact]
        public async Task CommitCorpIDCount_UnusedReservedSlotsAreReleased()
        {
            // 100 reserved, only 60 successfully added — 40 failures released
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter(0) { CorpIDReserve = 100 } };
            var manager = CreateManager(db);

            await manager.CommitCorpIDCount(100, 60, CancellationToken.None);

            Assert.Equal(0, db.Counter.CorpIDReserve);
            Assert.Equal(60, db.Counter.CorpIDCount);
        }

        // -------------------------------------------------------------------------
        // ReleaseCorpIDs
        // -------------------------------------------------------------------------

        /// <summary>
        /// Verifies that ReleaseCorpIDs decrements the CorpID count by the specified release amount.
        /// </summary>
        [Fact]
        public async Task ReleaseCorpIDs_DecrementsCorpIDCountByReleaseAmount()
        {
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter(0) { CorpIDCount = 100 } };
            var manager = CreateManager(db);

            int available = await manager.ReleaseCorpIDs(30, CancellationToken.None);

            Assert.Equal(70, db.Counter.CorpIDCount);
            Assert.Equal(TotalCap - 70, available);
        }

        /// <summary>
        /// Verifies that ReleaseCorpIDs prevents the CorpID count from going below zero in a drift scenario.
        /// </summary>
        [Fact]
        public async Task ReleaseCorpIDs_DoesNotGoBelowZero_OnDrift()
        {
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter(0) { CorpIDCount = 10 } };
            var manager = CreateManager(db);

            int available = await manager.ReleaseCorpIDs(50, CancellationToken.None);

            Assert.Equal(0, db.Counter.CorpIDCount);
            Assert.Equal(1000, available);
        }

        /// <summary>
        /// Verifies that ReleaseCorpIDs returns the updated available count after decrementing the CorpID count.
        /// </summary>
        [Fact]
        public async Task ReleaseCorpIDs_ReturnsUpdatedAvailable()
        {
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter(0) { CorpIDCount = 200 } };
            var manager = CreateManager(db);

            int available = await manager.ReleaseCorpIDs(50, CancellationToken.None);

            Assert.Equal(150, db.Counter.CorpIDCount);
            Assert.Equal(TotalCap - 150, available);
        }

        /// <summary>
        /// Verifies that ReleaseCorpIDs does not modify the CorpID reserve.
        /// </summary>
        [Fact]
        public async Task ReleaseCorpIDs_DoesNotAffectCorpIDReserve()
        {
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter(0) { CorpIDCount = 100, CorpIDReserve = 25 } };
            var manager = CreateManager(db);

            int available = await manager.ReleaseCorpIDs(40, CancellationToken.None);

            Assert.Equal(25, db.Counter.CorpIDReserve);
            Assert.Equal(TotalCap - 60 - 25, available);
        }
    }
}
