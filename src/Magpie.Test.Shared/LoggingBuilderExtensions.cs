using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Magpie.Test.Shared;

/// <summary>
/// Extension methods for configuring test logging.
/// </summary>
public static class LoggingBuilderExtensions
{
    /// <summary>
    /// Add a logger for tests
    /// </summary>
    public static ILoggingBuilder AddTestLogger(this ILoggingBuilder builder)
    {
        var value = TestContext.Parameters.Get("console-logs", "false");
        if (value == bool.TrueString)
        {
            builder.AddSimpleConsole();
        }
        return builder;
    }
}
