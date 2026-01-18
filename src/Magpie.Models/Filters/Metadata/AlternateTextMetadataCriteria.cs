namespace Magpie.Models.Filters.Metadata;

/// <summary>
/// Filter criteria for <see cref="Models.Metadata.AlternateTextMetadata"/>.
/// </summary>
public sealed record AlternateTextMetadataCriteria
{
    /// <inheritdoc cref="Models.Metadata.AlternateTextMetadata.Type" />
    public AnyOfCriteria<FieldCriterion<string>> Type { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.AlternateTextMetadata.Language" />
    public AnyOfCriteria<FieldCriterion<string>> Language { get; init; } = new();
    /// <inheritdoc cref="Models.Metadata.AlternateTextMetadata.Text" />
    public AnyOfCriteria<FieldCriterion<string>> Text { get; init; } = new();
}
