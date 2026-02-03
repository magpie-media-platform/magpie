using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="ExternalId"/>.
/// </summary>
public sealed record ExternalIdPatch
{
    /// <summary>
    /// The <see cref="ExternalId.Id"/> of the item to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The <see cref="ExternalId.Ordinal"/> to patch.
    /// </summary>
    public ScalarPatch<int>? Ordinal { get; init; }

    /// <summary>
    /// The <see cref="ExternalId.Value"/> to patch.
    /// </summary>
    public ScalarPatch<string>? Value { get; init; }

    /// <summary>
    /// The <see cref="ExternalId.Source"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? Source { get; init; }
}
