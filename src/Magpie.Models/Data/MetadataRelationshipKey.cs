using System;

namespace Magpie.Models.Data;

/// <summary>
/// The identity of a metadata relationship.
/// </summary>
/// <param name="RelatedMetadataContainerId">The id of the container at the other end of the link</param>
/// <param name="Kind">The kind of link, as a provider-defined slug</param>
/// <param name="Qualifier">The extra detail for the kind</param>
public readonly record struct MetadataRelationshipKey(
    Guid? RelatedMetadataContainerId,
    string Kind,
    string? Qualifier);
