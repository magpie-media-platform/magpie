using System;

using Magpie.Models.Api;

using Microsoft.Extensions.Logging;

namespace Magpie.Api.Logging;

/// <summary>
/// Logger provider that forwards entries to a <see cref="LogBuffer"/> so they can be served via the logs API endpoint.
/// </summary>
[ProviderAlias("LogBuffer")]
public sealed class LogBufferLoggerProvider(LogBuffer buffer) : ILoggerProvider
{
    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new LogBufferLogger(buffer, categoryName);

    /// <inheritdoc/>
    public void Dispose() { }
}

internal sealed class LogBufferLogger(LogBuffer buffer, string categoryName) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var entry = new LogEntry
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow,
            Level = MapLevel(logLevel),
            Source = categoryName,
            Message = formatter(state, exception),
            Exception = exception?.ToString()
        };

        buffer.Add(entry);
    }

    private static string MapLevel(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace or LogLevel.Debug => "debug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error or LogLevel.Critical => "error",
        _ => "info"
    };
}
