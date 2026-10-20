using CorporateIdentifierSync.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CorporateIdentifierSync.Tests.BlobLeaseSingletonLockTests
{
    [Collection("EnvVarTests")]
    public class BlobLeaseSingletonLockTests
    {
        private static void ClearBlobEnvVars()
        {
            Environment.SetEnvironmentVariable("AzureWebJobsStorage", null);
            Environment.SetEnvironmentVariable("AzureWebJobsStorage__blobServiceUri", null);
            Environment.SetEnvironmentVariable("AzureWebJobsStorage__accountName", null);
        }

        /// <summary>
        /// Verifies that the constructor throws an InvalidOperationException when no Azure storage environment variables are configured.
        /// </summary>
        [Fact]
        public void Constructor_WhenNoStorageConfigured_ThrowsInvalidOperationException()
        {
            ClearBlobEnvVars();

            try
            {
                var ex = Assert.Throws<InvalidOperationException>(
                    () => new BlobLeaseSingletonLock(NullLogger<BlobLeaseSingletonLock>.Instance));

                Assert.Contains("AzureWebJobsStorage", ex.Message);
            }
            finally
            {
                ClearBlobEnvVars();
            }
        }

        /// <summary>
        /// Verifies that the constructor does not throw when a connection string is provided via the AzureWebJobsStorage environment variable.
        /// </summary>
        [Fact]
        public void Constructor_WhenConnectionStringConfigured_DoesNotThrow()
        {
            Environment.SetEnvironmentVariable("AzureWebJobsStorage",
                "DefaultEndpointsProtocol=https;AccountName=fake;AccountKey=ZmFrZWtleQ==;EndpointSuffix=core.windows.net");

            try
            {
                // Act — no network call in the constructor, just object creation
                var sut = new BlobLeaseSingletonLock(NullLogger<BlobLeaseSingletonLock>.Instance);
                Assert.NotNull(sut);
            }
            finally
            {
                ClearBlobEnvVars();
            }
        }

        /// <summary>
        /// Verifies that the constructor does not throw when a blob service URI is provided via AzureWebJobsStorage__blobServiceUri.
        /// </summary>
        [Fact]
        public void Constructor_WhenBlobServiceUriConfigured_DoesNotThrow()
        {
            Environment.SetEnvironmentVariable("AzureWebJobsStorage__blobServiceUri",
                "https://fakeaccount.blob.core.windows.net");

            try
            {
                var sut = new BlobLeaseSingletonLock(NullLogger<BlobLeaseSingletonLock>.Instance);
                Assert.NotNull(sut);
            }
            finally
            {
                ClearBlobEnvVars();
            }
        }

        /// <summary>
        /// Verifies that the constructor does not throw when a storage account name is provided via AzureWebJobsStorage__accountName.
        /// </summary>
        [Fact]
        public void Constructor_WhenAccountNameConfigured_DoesNotThrow()
        {
            Environment.SetEnvironmentVariable("AzureWebJobsStorage__accountName", "fakeaccount");

            try
            {
                var sut = new BlobLeaseSingletonLock(NullLogger<BlobLeaseSingletonLock>.Instance);
                Assert.NotNull(sut);
            }
            finally
            {
                ClearBlobEnvVars();
            }
        }
    }
}