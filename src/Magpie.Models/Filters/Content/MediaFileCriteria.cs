using System;

namespace Magpie.Models.Filters.Content;

/// <summary>
/// Filter criteria for media files.
/// </summary>
public sealed record MediaFileCriteria
{
    /// <inheritdoc cref="Models.Content.MediaFile.Kind" />
    public AnyOfCriteria<FieldCriterion<string>> Kind { get; init; } = new();
    /// <inheritdoc cref="Models.Content.MediaFile.MimeType" />
    public AnyOfCriteria<FieldCriterion<string>> MimeType { get; init; } = new();
    /// <inheritdoc cref="Models.Content.MediaFile.RelativePath" />
    public AnyOfCriteria<FieldCriterion<string>> RelativePath { get; init; } = new();
    /// <inheritdoc cref="Models.Content.MediaFile.Size" />
    public AnyOfCriteria<FieldCriterion<long>> Size { get; init; } = new();
    /// <inheritdoc cref="Models.Content.MediaFile.CreatedAt" />
    public AnyOfCriteria<FieldCriterion<DateTimeOffset>> CreatedAt { get; init; } = new();
    /// <inheritdoc cref="Models.Content.MediaFile.MediaInfo" />
    public AnyOfCriteria<MediaInfoCriteria> MediaInfo { get; init; } = new();
}
