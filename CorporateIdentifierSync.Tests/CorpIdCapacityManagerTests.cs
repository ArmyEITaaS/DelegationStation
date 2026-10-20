using CorporateIdentifierSync;
using CorporateIdentifierSync.Interfaces;
using DelegationStationShared.Models;
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
        public Task<int> GetSyncedDeviceCountAsync() => throw new NotImplementedException();
    }

    public class CorpIdCapacityManagerTests
    {
        private const int MaxCorpIDs = 1000;

        private static CorpIdCapacityManager CreateManager(FakeCosmosDbService db)
            => new CorpIdCapacityManager(db, NullLogger.Instance, MaxCorpIDs);

        // -------------------------------------------------------------------------
        // GetAvailableCorpIDCount
        // -------------------------------------------------------------------------

        /// <summary>
        /// Verifies that GetAvailableCorpIDCount returns the correct available count
        /// based on the current CorpIDCount and CorpIDReserve values.
        /// </summary>
        [Theory]
        //          CorpIDCount, CorpIDReserve, ExpectedAvailable
        [InlineData(0,          0,   MaxCorpIDs)] // empty counter — full cap available
        [InlineData(300,      100,          600)] // deducts both count and reserve
        [InlineData(MaxCorpIDs, 0,            0)] // at cap — nothing available
        [InlineData(800,      300,         -100)] // total exceeds cap — returns negative (no clamping)
        public async Task GetAvailableCorpIDCount_ReturnsExpectedAvailable(
            int corpIDCount, int corpIDReserve, int expected)
        {
            var db = new FakeCosmosDbService
            {
                Counter = new CorpIDCounter(0) { CorpIDCount = corpIDCount, CorpIDReserve = corpIDReserve }
            };
            var manager = CreateManager(db);

            int available = await manager.GetAvailableCorpIDCount(CancellationToken.None);

            Assert.Equal(expected, available);
        }

        // -------------------------------------------------------------------------
        // ReserveCorpIDs
        // -------------------------------------------------------------------------

        /// <summary>
        /// Verifies that ReserveCorpIDs reserves the correct number of slots and persists
        /// the updated reserve — handling full capacity, partial capacity, no capacity,
        /// and additive accumulation to an existing reserve.
        /// </summary>
        [Theory]
        //                startCount  startReserve  requested  expectedReserved  expectedFinalReserve
        [InlineData(           0,         0,           50,            50,               50)] // sufficient capacity
        [InlineData(MaxCorpIDs,           0,           10,             0,                0)] // at cap — nothing reserved
        [InlineData(         980,         0,           50,            20,               20)] // partial capacity — clamped to available
        [InlineData(           0,       500,           25,            25,              525)] // additive: persists to existing reserve
        [InlineData(         200,        50,            0,             0,               50)] // zero request — no-op
        [InlineData(           0,        50,          -10,           -10,               40)] // negative count — decrements reserve (no guard)
        public async Task ReserveCorpIDs_ReservesExpectedCount(
            int startingCount, int startingReserve, int requested,
            int expectedReserved, int expectedFinalReserve)
        {
            var db = new FakeCosmosDbService
            {
                Counter = new CorpIDCounter(0) { CorpIDCount = startingCount, CorpIDReserve = startingReserve }
            };
            var manager = CreateManager(db);

            int reserved = await manager.ReserveCorpIDs(requested, CancellationToken.None);

            Assert.Equal(expectedReserved, reserved);
            Assert.Equal(expectedFinalReserve, db.Counter.CorpIDReserve);
        }

        // -------------------------------------------------------------------------
        // CommitCorpIDCount
        // -------------------------------------------------------------------------

        /// <summary>
        /// Verifies that CommitCorpIDCount correctly decrements the reserve, increments the count,
        /// and returns the remaining available slots — including drift and partial-sync scenarios.
        /// </summary>
        [Theory]
        //               startCount  startReserve  reserved  synced  expectedReserve  expectedCount  expectedAvailable
        [InlineData(           0,         50,         50,      40,         0,             40,              960)] // normal commit
        [InlineData(           0,        100,        100,      80,         0,             80,              920)] // returns remaining available
        [InlineData(           0,         10,         50,      10,         0,             10,              990)] // reserve clamped to 0 on drift
        [InlineData(           0,        100,        100,      60,         0,             60,              940)] // unused reserved slots released
        [InlineData(           0,          0,          0,       0,         0,              0,             1000)] // zero values — no-op
        [InlineData(           0,         50,        -10,       0,        60,              0,              940)] // negative reserved — increases reserve (no guard)
        [InlineData(         100,          0,          0,     -30,         0,             70,              930)] // negative added — decrements count (no guard)
        [InlineData(         950,        100,        100,     200,         0,           1150,             -150)] // exceeds cap — available goes negative (no clamping)
        public async Task CommitCorpIDCount_UpdatesCounterCorrectly(
            int startingCount, int startingReserve, int reserved, int synced,
            int expectedReserve, int expectedCount, int expectedAvailable)
        {
            var db = new FakeCosmosDbService { Counter = new CorpIDCounter(0) { CorpIDCount = startingCount, CorpIDReserve = startingReserve } };
            var manager = CreateManager(db);

            int available = await manager.CommitCorpIDCount(reserved, synced, CancellationToken.None);

            Assert.Equal(expectedReserve, db.Counter.CorpIDReserve);
            Assert.Equal(expectedCount, db.Counter.CorpIDCount);
            Assert.Equal(expectedAvailable, available);
        }

        // -------------------------------------------------------------------------
        // ReleaseCorpIDs
        // -------------------------------------------------------------------------

        /// <summary>
        /// Verifies that ReleaseCorpIDs correctly decrements the CorpID count, leaves the reserve
        /// unchanged, returns updated available slots, and clamps to zero on drift.
        /// </summary>
        [Theory]
        //               startCount  startReserve  release  expectedCount  expectedReserve  expectedAvailable
        [InlineData(        100,          0,          30,        70,              0,              930)] // normal decrement
        [InlineData(         10,          0,          50,         0,              0,             1000)] // clamped to 0 on drift
        [InlineData(        200,          0,          50,       150,              0,              850)] // returns updated available
        [InlineData(        100,         25,          40,        60,             25,              915)] // reserve is not affected
        [InlineData(        200,         30,           0,       200,             30,              770)] // zero release — no-op
        [InlineData(        100,          0,         -50,       150,              0,              850)] // negative release — increases count (no guard)
        public async Task ReleaseCorpIDs_UpdatesCounterCorrectly(
            int startingCount, int startingReserve, int release,
            int expectedCount, int expectedReserve, int expectedAvailable)
        {
            var db = new FakeCosmosDbService
            {
                Counter = new CorpIDCounter(0) { CorpIDCount = startingCount, CorpIDReserve = startingReserve }
            };
            var manager = CreateManager(db);

            int available = await manager.ReleaseCorpIDs(release, CancellationToken.None);

            Assert.Equal(expectedCount, db.Counter.CorpIDCount);
            Assert.Equal(expectedReserve, db.Counter.CorpIDReserve);
            Assert.Equal(expectedAvailable, available);
        }

        // -------------------------------------------------------------------------
        // Constructor Edge Cases
        // -------------------------------------------------------------------------

        [Fact]
        public async Task Constructor_ZeroCap_NothingAvailableOrReserved()
        {
            var db = new FakeCosmosDbService
            {
                Counter = new CorpIDCounter(0) { CorpIDCount = 0, CorpIDReserve = 0 }
            };
            var manager = new CorpIdCapacityManager(db, NullLogger.Instance, 0);

            int available = await manager.GetAvailableCorpIDCount(CancellationToken.None);
            int reserved = await manager.ReserveCorpIDs(10, CancellationToken.None);

            Assert.Equal(0, available);
            Assert.Equal(0, reserved);
        }

        [Fact]
        public void Constructor_NegativeCap_ThrowsArgumentOutOfRangeException()
        {
            var db = new FakeCosmosDbService
            {
                Counter = new CorpIDCounter(0) { CorpIDCount = 0, CorpIDReserve = 0 }
            };

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CorpIdCapacityManager(db, NullLogger.Instance, -100));
        }
    }
}
