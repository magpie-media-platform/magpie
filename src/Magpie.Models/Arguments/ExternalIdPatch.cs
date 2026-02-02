using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="ExternalId"/>.
/// </summary>
/// <param name="Id">The <see cref="ExternalId.Id"/> of the item to patch.</param>
/// <param name="Ordinal">The <see cref="ExternalId.Ordinal"/> patch</param>
/// <param name="Value">The <see cref="ExternalId.Value"/> patch</param>
/// <param name="Source">The <see cref="ExternalId.Source"/> patch</param>
public sealed record ExternalIdPatch(
    Guid Id,
    ScalarPatch<int>? Ordinal = null,
    ScalarPatch<string>? Value = null,
    ScalarPatch<string?>? Source = null);
