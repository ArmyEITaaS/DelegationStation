using DelegationStationShared.Models;

namespace CorporateIdentifierSync.Tests.CorpIDCounterTests
{
    public class CorpIDCounterTests
    {
        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(1000)]
        [InlineData(-1)]
        public void Constructor_SetsCorpIDCountToPassedValue(int count)
        {
            var counter = new CorpIDCounter(count);

            Assert.Equal(count, counter.CorpIDCount);
        }

        [Fact]
        public void Constructor_SetsCorpIDReserveToZero()
        {
            var counter = new CorpIDCounter(100);

            Assert.Equal(0, counter.CorpIDReserve);
        }

        [Fact]
        public void Constructor_SetsPartitionKeyToCorpIDCounter()
        {
            var counter = new CorpIDCounter(0);

            Assert.Equal("CorpIDCounter", counter.PartitionKey);
        }

        [Fact]
        public void Constructor_GeneratesNonEmptyId()
        {
            var counter = new CorpIDCounter(0);

            Assert.NotEqual(Guid.Empty, counter.id);
        }

        [Fact]
        public void Constructor_EachInstance_HasUniqueId()
        {
            var counter1 = new CorpIDCounter(0);
            var counter2 = new CorpIDCounter(0);

            Assert.NotEqual(counter1.id, counter2.id);
        }

        [Fact]
        public void Constructor_SetsCreatedDTToApproximatelyUtcNow()
        {
            var before = DateTime.UtcNow;
            var counter = new CorpIDCounter(0);
            var after = DateTime.UtcNow;

            Assert.InRange(counter.CreatedDT, before, after);
        }

        [Fact]
        public void Constructor_SetsModifiedDTToApproximatelyUtcNow()
        {
            var before = DateTime.UtcNow;
            var counter = new CorpIDCounter(0);
            var after = DateTime.UtcNow;

            Assert.InRange(counter.ModifiedDT, before, after);
        }

        // -------------------------------------------------------------------------
        // GetTotal
        // -------------------------------------------------------------------------

        [Theory]
        //              count  reserve  expected
        [InlineData(      0,       0,        0)]
        [InlineData(     10,       0,       10)]
        [InlineData(      0,       5,        5)]
        [InlineData(    100,      25,      125)]
        [InlineData(    -10,       5,       -5)]
        public void GetTotal_ReturnsCorpIDCountPlusCorpIDReserve(
            int count, int reserve, int expected)
        {
            var counter = new CorpIDCounter(count) { CorpIDReserve = reserve };

            Assert.Equal(expected, counter.GetTotal());
        }

        // -------------------------------------------------------------------------
        // ToString
        // -------------------------------------------------------------------------

        [Fact]
        public void ToString_ReturnsExpectedFormat()
        {
            var counter = new CorpIDCounter(10) { CorpIDReserve = 3 };

            Assert.Equal("CorpIDCount: 10, CorpIDReserve: 3", counter.ToString());
        }

        [Fact]
        public void ToString_WithZeroValues_ReturnsExpectedFormat()
        {
            var counter = new CorpIDCounter(0);

            Assert.Equal("CorpIDCount: 0, CorpIDReserve: 0", counter.ToString());
        }
    }
}