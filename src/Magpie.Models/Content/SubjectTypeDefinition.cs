using System;
using System.Collections.Generic;

using Magpie.Models.Metadata;

namespace Magpie.Models.Content;

/// <summary>
/// Defines a subject type.
/// </summary>
public sealed record SubjectTypeDefinition
{
    /// <summary>
    /// The unique identifier of the subject type definition.
    /// </summary>
    public Guid Id { get; init; }
    /// <summary>
    /// The version of the subject type definition.
    /// </summary>
    public int? Version { get; init; }
    /// <summary>
    /// The display name of the subject type.
    /// </summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>
    /// The custom metadata fields defined for this subject type.
    /// </summary>
    public required IReadOnlyList<CustomMetadataFieldDefinition> CustomMetadataFields { get; init; }
    /// <summary>
    /// The metadata fields supported by this subject type.
    /// </summary>
    public MetadataField SupportedMetadataFields { get; init; } = MetadataField.All;
}
