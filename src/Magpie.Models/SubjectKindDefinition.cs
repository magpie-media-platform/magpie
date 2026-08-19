using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models;

/// <summary>
/// The definition of a subject kind.
/// </summary>
public sealed record SubjectKindDefinition
{
    /// <summary>
    /// The stable slug of the subject kind.
    /// </summary>
    public required string Slug { get; init; }

    /// <summary>
    /// The singular display name of the subject kind.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// The plural display name of the subject kind.
    /// </summary>
    public string? PluralDisplayName { get; init; }

    /// <summary>
    /// The name of the icon for the subject kind.
    /// </summary>
    public string? IconName { get; init; }

    /// <summary>
    /// The standard metadata fields that this subject kind supports.
    /// </summary>
    public MetadataField SupportedMetadataFields { get; init; } = MetadataField.All;

    /// <summary>
    /// The custom metadata fields of this subject kind.
    /// </summary>
    public required IReadOnlyList<CustomMetadataDefinition> CustomMetadataFields { get; init; }

    /// <summary>
    /// The metadata fields that identify matching subjects when external IDs do not.
    /// </summary>
    public MetadataField FallbackIdentityFields { get; init; }
}
