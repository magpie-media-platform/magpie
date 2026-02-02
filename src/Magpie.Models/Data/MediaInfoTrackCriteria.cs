using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for <see cref="MediaInfoTrack"/> 
/// </summary>
public sealed record MediaInfoTrackCriteria
{
    /// <inheritdoc cref="MediaInfoTrack.Type" />
    public PropertyCriterion<string> Type { get; init; } = new();
    /// <inheritdoc cref="MediaInfoTrack.Name" />
    public PropertyCriterion<string> Name { get; init; } = new();
    /// <inheritdoc cref="MediaInfoTrack.Properties" />
    public PropertyDictionaryCriteria Properties { get; init; } = new();
}
