using System;

using Magpie.Models.Filters.Metadata;

namespace Magpie.Models.Filters;

/// <summary>
/// Patch for <see cref="MetadataFilter"/>.
/// </summary>
public sealed record MetadataFilterPatch
{
    /// <inheritdoc cref="MetadataFilter.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="MetadataFilter.Name" />
    public FieldPatch<string>? Name { get; init; }
    /// <inheritdoc cref="MetadataFilter.Criteria" />
    public FieldPatch<MetadataContributionCriteria>? Criteria { get; init; }
    /// <inheritdoc cref="MetadataFilter.Tags" />
    public SetPatch<string, string>? Tags { get; init; }
}
