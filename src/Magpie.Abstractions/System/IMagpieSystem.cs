namespace Magpie.Abstractions.System;

/// <summary>
/// Provides access to Magpie system-level paths and configuration.
/// </summary>
public interface IMagpieSystem
{
    /// <summary>
    /// The root directory for the Magpie installation.
    /// </summary>
    string SystemRoot { get; }
}
