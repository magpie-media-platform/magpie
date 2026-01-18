using System;

using Magpie.Models.Filters.Content;

namespace Magpie.Models.Filters;

/// <summary>
/// Patch for <see cref="ContentFilter"/>.
/// </summary>
public sealed record ContentFilterPatch
{
    /// <inheritdoc cref="ContentFilter.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="ContentFilter.Name" />
    public FieldPatch<string>? Name { get; init; }
    /// <inheritdoc cref="ContentFilter.Criteria" />
    public FieldPatch<ContentItemCriteria>? Criteria { get; init; }
    /// <inheritdoc cref="ContentFilter.Tags" />
    public SetPatch<string, string>? Tags { get; init; }
}
