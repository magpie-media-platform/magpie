using System;

namespace Magpie.Models.Filters.Content;

/// <summary>
/// Filter criteria for media info.
/// </summary>
public sealed record MediaInfoCriteria
{
    /// <inheritdoc cref="Models.Content.MediaInfo.Container" />
    public AnyOfCriteria<FieldCriterion<string>> Container { get; init; } = new();
    /// <inheritdoc cref="Models.Content.MediaInfo.Duration" />
    public AnyOfCriteria<FieldCriterion<TimeSpan>> Duration { get; init; } = new();
    /// <inheritdoc cref="Models.Content.MediaInfo.VideoCodec" />
    public AnyOfCriteria<FieldCriterion<string>> VideoCodec { get; init; } = new();
    /// <inheritdoc cref="Models.Content.MediaInfo.VideoWidth" />
    public AnyOfCriteria<FieldCriterion<int>> VideoWidth { get; init; } = new();
    /// <inheritdoc cref="Models.Content.MediaInfo.VideoHeight" />
    public AnyOfCriteria<FieldCriterion<int>> VideoHeight { get; init; } = new();
    /// <inheritdoc cref="Models.Content.MediaInfo.VideoBitrate" />
    public AnyOfCriteria<FieldCriterion<int>> VideoBitrate { get; init; } = new();
    /// <inheritdoc cref="Models.Content.MediaInfo.AudioCodec" />
    public AnyOfCriteria<FieldCriterion<string>> AudioCodec { get; init; } = new();
    /// <inheritdoc cref="Models.Content.MediaInfo.AudioChannels" />
    public AnyOfCriteria<FieldCriterion<int>> AudioChannels { get; init; } = new();
    /// <inheritdoc cref="Models.Content.MediaInfo.AudioBitrate" />
    public AnyOfCriteria<FieldCriterion<int>> AudioBitrate { get; init; } = new();
}
