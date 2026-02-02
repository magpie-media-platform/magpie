using System;
using System.IO;

using Microsoft.Extensions.Logging;

namespace Magpie.Test.Shared;

internal sealed class TextWriterLogger(string categoryName, TextWriter writer) : ILogger
{
    public sealed class Provider(TextWriter writer) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new TextWriterLogger(categoryName, writer);

        public void Dispose() { }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        var levelString = logLevel switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => "????"
        };

        writer.WriteLine($"{levelString}: {categoryName} {message}");
        if (exception is not null)
        {
            writer.WriteLine(exception.ToString());
        }
    }
}
