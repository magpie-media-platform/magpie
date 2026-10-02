namespace Magpie.Abstractions;

/// <summary>
/// The error for a shutdown request that failed.
/// </summary>
/// <param name="message">The text that describes the error</param>
public sealed class MagpieShutdownException(string message = "A shutdown request failed.") : MagpieException(message);
