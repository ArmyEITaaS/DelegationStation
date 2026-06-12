using Xunit;

namespace CorporateIdentifierSync.Tests.TestCollections
{
    /// <summary>
    /// Serializes all test classes that read or write process-level environment
    /// variables. xUnit runs every class in the same named collection on a single
    /// thread, eliminating race conditions between SetSyncEnabled / ClearEnvVars
    /// and EnvVarScope used in different test classes.
    /// </summary>
    [CollectionDefinition("EnvVarTests", DisableParallelization = true)]
    public sealed class EnvVarTestsCollection { }
}