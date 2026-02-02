using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for <see cref="MediaInfo"/>
/// </summary>
public sealed record MediaInfoCriteria
{
    /// <inheritdoc cref="MediaInfo.Tracks" />
    public AllCriteria<MediaInfoTrackCriteria> Tracks { get; init; } = new();
}
