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
    public required string Slug { get; set; }

    /// <summary>
    /// The singular display name of the subject kind.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// The plural display name of the subject kind.
    /// </summary>
    public string? PluralDisplayName { get; set; }

    /// <summary>
    /// The name of the icon for the subject kind.
    /// </summary>
    public string? IconName { get; set; }

    /// <summary>
    /// The standard metadata fields that this subject kind supports.
    /// </summary>
    public MetadataField SupportedMetadataFields { get; set; } = MetadataField.All;

    /// <summary>
    /// The custom metadata fields of this subject kind.
    /// </summary>
    public required IReadOnlyList<CustomMetadataDefinition> CustomMetadataFields { get; set; }

    /// <summary>
    /// The metadata fields that identify matching subjects when external IDs do not.
    /// </summary>
    public MetadataField FallbackIdentityFields { get; set; }
}
