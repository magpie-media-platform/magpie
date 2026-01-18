using System;
using System.Collections.Generic;

namespace Magpie.Models.MediaServers;

/// <summary>
/// Patch for <see cref="MediaServer"/>.
/// </summary>
public sealed record MediaServerPatch
{
    /// <inheritdoc cref="MediaServer.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="MediaServer.Name" />
    public FieldPatch<string>? Name { get; init; }
    /// <inheritdoc cref="MediaServer.Enabled" />
    public FieldPatch<bool>? Enabled { get; init; }
    /// <inheritdoc cref="MediaServer.Settings" />
    public FieldPatch<IReadOnlyDictionary<string, string>>? Settings { get; init; }
}
