using DelegationStationShared.Extensions;
using Microsoft.Extensions.Logging;

namespace DelegationSharedLibrary.Tests
{
    /// <summary>
    /// Minimal <see cref="ILogger"/> implementation that records every logged
    /// entry so tests can assert on the resulting log level, message, and exception.
    /// </summary>
    public class RecordingLogger : ILogger
    {
        public record LogEntry(LogLevel Level, string Message, Exception? Exception);

        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    public class LoggerExtensionsTests
    {
        [Fact]
        public void DSLogCritical_LogsAtCriticalLevel_WithFormattedMessage()
        {
            var logger = new RecordingLogger();

            logger.DSLogCritical("something bad happened", "MyMethod");

            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Critical, entry.Level);
            Assert.Equal("[CRITICAL} (MyMethod) something bad happened", entry.Message);
        }

        [Fact]
        public void DSLogError_LogsAtErrorLevel_WithFormattedMessage()
        {
            var logger = new RecordingLogger();

            logger.DSLogError("error occurred", "MyMethod");

            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Error, entry.Level);
            Assert.Equal("[ERROR] (MyMethod) error occurred", entry.Message);
        }

        [Fact]
        public void DSLogInformation_LogsAtInformationLevel_WithFormattedMessage()
        {
            var logger = new RecordingLogger();

            logger.DSLogInformation("informational message", "MyMethod");

            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Information, entry.Level);
            Assert.Equal("[INFO] (MyMethod) informational message", entry.Message);
        }

        [Fact]
        public void DSLogWarning_LogsAtWarningLevel_WithFormattedMessage()
        {
            var logger = new RecordingLogger();

            logger.DSLogWarning("warning message", "MyMethod");

            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Equal("[WARNING] (MyMethod) warning message", entry.Message);
        }

        [Fact]
        public void DSLogDebug_LogsAtDebugLevel_WithFormattedMessage()
        {
            var logger = new RecordingLogger();

            logger.DSLogDebug("debug message", "MyMethod");

            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Debug, entry.Level);
            Assert.Equal("[DEBUG] (MyMethod) debug message", entry.Message);
        }

        [Fact]
        public void DSLogTrace_LogsAtTraceLevel_WithFormattedMessage()
        {
            var logger = new RecordingLogger();

            logger.DSLogTrace("trace message", "MyMethod");

            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Trace, entry.Level);
            Assert.Equal("[TRACE] (MyMethod) trace message", entry.Message);
        }

        [Fact]
        public void DSLogAudit_LogsAtInformationLevel_WithFormattedMessage()
        {
            var logger = new RecordingLogger();

            logger.DSLogAudit("audit message", "MyMethod");

            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Information, entry.Level);
            Assert.Equal("[AUDIT] (MyMethod) audit message", entry.Message);
        }

        [Fact]
        public void DSLogException_LogsAtErrorLevel_WithMessageAndStackTrace()
        {
            var logger = new RecordingLogger();
            Exception ex;
            try
            {
                throw new InvalidOperationException("boom");
            }
            catch (InvalidOperationException caught)
            {
                ex = caught;
            }

            logger.DSLogException("failure while processing", ex, "MyMethod");

            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Error, entry.Level);
            // DSLogException embeds the exception details in the formatted message rather
            // than passing the Exception object through to the underlying Log call.
            Assert.Null(entry.Exception);
            Assert.Contains("[EXCEPTION] (MyMethod) failure while processing", entry.Message);
            Assert.Contains(ex.Message, entry.Message);
            Assert.Contains("Stack Trace", entry.Message);
            Assert.Contains(ex.StackTrace!, entry.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void DSLogInformation_DefaultsMethodNameToEmptyString(string? methodName)
        {
            var logger = new RecordingLogger();

            logger.DSLogInformation("message with no method name", methodName!);

            var entry = Assert.Single(logger.Entries);
            Assert.Equal("[INFO] () message with no method name", entry.Message);
        }
    }
}
