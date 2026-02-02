using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// A track within a media file.
/// </summary>
public record MediaInfoTrack
{
    /// <summary>
    /// The type of track (e.g. Video, Audio).
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// The name of the track.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The properties of the track.
    /// </summary>
    public Dictionary<string, string> Properties { get; init; } = [];
}
