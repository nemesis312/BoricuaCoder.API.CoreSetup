using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace BoricuaCoder.API.CoreSetup.Tests.Integration;

internal sealed record LogRecord(LogLevel Level, string Category, string Message);

internal sealed class TestLogCollector
{
    private readonly List<LogRecord> _records = new();

    public IReadOnlyList<LogRecord> GetSnapshot()
    {
        lock (_records) return _records.ToList();
    }

    internal void Add(LogLevel level, string category, string message)
    {
        lock (_records) _records.Add(new LogRecord(level, category, message));
    }
}

internal sealed class TestLoggerProvider(TestLogCollector collector) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new TestLogger(categoryName, collector);
    public void Dispose() { }
}

internal sealed class TestLogger(string category, TestLogCollector collector) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => collector.Add(logLevel, category, formatter(state, exception));
}

internal static class TestLoggingExtensions
{
    internal static ILoggingBuilder AddTestLogging(
        this ILoggingBuilder builder, TestLogCollector collector)
    {
        builder.Services.TryAddSingleton(collector);
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider>(new TestLoggerProvider(collector)));
        return builder;
    }
}
