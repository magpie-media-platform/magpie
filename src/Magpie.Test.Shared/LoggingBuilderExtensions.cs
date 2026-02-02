using System;
using System.IO;

using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Magpie.Test.Shared;

/// <summary>
/// Extension methods for configuring test logging.
/// </summary>
public static class LoggingBuilderExtensions
{
    /// <summary>
    /// Add a logger for tests that writes to the specified TextWriter.
    /// </summary>
    public static ILoggingBuilder AddTestLogger(this ILoggingBuilder builder)
    {
        var value = TestContext.Parameters.Get("test-logger", "False");
        if (value != bool.TrueString)
        {
            return builder;
        }
        var testLoggerWriter = TestContext.Parameters.Get("test-logger-writer", nameof(TestContext)) switch
        {
            nameof(Console) => Console.Out,
            nameof(TestContext) => TestContext.Out,
            _ => TextWriter.Null
        };
        builder.AddProvider(new TextWriterLogger.Provider(testLoggerWriter));

        return builder;
    }
}
