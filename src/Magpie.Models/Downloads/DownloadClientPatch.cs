using System;
using System.Collections.Generic;

namespace Magpie.Models.Downloads;

/// <summary>
/// Patch for <see cref="DownloadClient"/>.
/// </summary>
public sealed record DownloadClientPatch
{
    /// <inheritdoc cref="DownloadClient.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="DownloadClient.Name" />
    public FieldPatch<string>? Name { get; init; }
    /// <inheritdoc cref="DownloadClient.Enabled" />
    public FieldPatch<bool>? Enabled { get; init; }
    /// <inheritdoc cref="DownloadClient.Tags" />
    public SetPatch<string, string>? Tags { get; init; }
    /// <inheritdoc cref="DownloadClient.Settings" />
    public FieldPatch<IReadOnlyDictionary<string, string?>>? Settings { get; init; }
}
