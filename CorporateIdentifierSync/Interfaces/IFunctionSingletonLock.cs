namespace CorporateIdentifierSync.Interfaces
{
    /// <summary>
    /// Provides a distributed singleton lock for Azure Functions using Azure Blob leases.
    /// Acquire a lock at the start of a function; dispose it (via await using) when done.
    /// </summary>
    public interface IFunctionSingletonLock
    {
        /// <summary>
        /// Tries to acquire the named singleton lock. Returns an IAsyncDisposable handle on success,
        /// or null if another instance currently holds the lock.
        /// </summary>
        /// <param name="lockName">Logical name of the lock (e.g. function name). Used as the blob name.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task<IAsyncDisposable?> TryAcquireAsync(string lockName, CancellationToken cancellationToken = default);
    }
}