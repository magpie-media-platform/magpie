using System;

namespace Magpie.Models.Metadata;

/// <summary>
/// A relationship to another entity.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="RelatedMetadataId">The related metadata identifier.</param>
/// <param name="Ordinal">The ordering within the list.</param>
/// <param name="Relation">The relation type (e.g., Actor, Director, etc.).</param>
/// <param name="Qualifier">The optional qualifier for the relationship (e.g., character name of a role).</param>
public sealed record RelationshipMetadata(
    Guid Id,
    Guid RelatedMetadataId,
    int Ordinal,
    string Relation,
    string? Qualifier);
