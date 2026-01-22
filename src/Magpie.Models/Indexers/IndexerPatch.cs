using System;

namespace Magpie.Models.Indexers;

/// <summary>
/// Patch for <see cref="Indexer"/>.
/// </summary>
public sealed record IndexerPatch
{
    /// <inheritdoc cref="Indexer.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="Indexer.Name" />
    public FieldPatch<string>? Name { get; init; }
    /// <inheritdoc cref="Indexer.Enabled" />
    public FieldPatch<bool>? Enabled { get; init; }
    /// <inheritdoc cref="Indexer.Tags" />
    public SetPatch<string, string>? Tags { get; init; }
    /// <inheritdoc cref="Indexer.Settings" />
    public DictionaryPatch<string, string?, string?>? Settings { get; init; }
}
