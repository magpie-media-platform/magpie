namespace Magpie.Models.Releases;

/// <summary>
/// The proper, repack, and version markers that a release title holds.
/// </summary>
/// <param name="Version">The version number in the title</param>
/// <param name="IsProper">Whether the title marks a proper</param>
/// <param name="IsRepack">Whether the title marks a repack</param>
public readonly record struct ReleaseRevision(int Version, bool IsProper, bool IsRepack);
