public abstract class BunitTestContext : BunitContext
{
    [TestCleanup]
    public void TearDown() => Dispose();
}