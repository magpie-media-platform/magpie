namespace Magpie.Models.System;

/// <summary>
/// Information about the running system.
/// </summary>
/// <param name="Version">The application version.</param>
/// <param name="Runtime">The .NET runtime description.</param>
/// <param name="OperatingSystem">The operating system description.</param>
/// <param name="Architecture">The processor architecture.</param>
public sealed record SystemInfo(
    string Version,
    string Runtime,
    string OperatingSystem,
    string Architecture);
