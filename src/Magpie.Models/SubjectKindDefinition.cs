using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models;

/// <summary>
/// Defines a subject kind.
/// </summary>
public sealed record SubjectKindDefinition
{
    /// <summary>
    /// The unique url-safe slug identifier of the subject kind
    /// </summary>
    public required string Slug { get; init; }

    /// <summary>
    /// The display name of the subject type.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// The metadata fields supported by the subject type.
    /// </summary>
    public MetadataField SupportedMetadataFields { get; init; } = MetadataField.All;

    /// <summary>
    /// The custom metadata fields defined for the subject type.
    /// </summary>
    public required IReadOnlyList<CustomMetadataDefinition> CustomMetadataFields { get; init; }
}
