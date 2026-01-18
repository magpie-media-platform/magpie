using System;
using System.Collections.Generic;

namespace Magpie.Models.Indexers;

/// <summary>
/// Patch for <see cref="IndexerFeed"/>.
/// </summary>
public sealed record IndexerFeedPatch
{
    /// <inheritdoc cref="IndexerFeed.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="IndexerFeed.Name" />
    public FieldPatch<string>? Name { get; init; }
    /// <inheritdoc cref="IndexerFeed.Description" />
    public FieldPatch<string?>? Description { get; init; }
    /// <inheritdoc cref="IndexerFeed.Enabled" />
    public FieldPatch<bool>? Enabled { get; init; }
    /// <inheritdoc cref="IndexerFeed.Settings" />
    public FieldPatch<IReadOnlyDictionary<string, string>>? Settings { get; init; }
}
