using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="IndexerConnection"/>.
/// </summary>
/// <param name="Id">The <see cref="IndexerConnection.Id"/> of the item to patch.</param>
/// <param name="Name">The <see cref="IndexerConnection.Name"/> patch</param>
/// <param name="Enabled">The <see cref="IndexerConnection.Enabled"/> patch</param>
/// <param name="Settings">The <see cref="IndexerConnection.Settings"/> patch</param>
/// <param name="Tags">The <see cref="Tag"/>s patch</param>
public sealed record IndexerConnectionPatch(
    Guid Id,
    ScalarPatch<string>? Name = null,
    ScalarPatch<bool>? Enabled = null,
    DictionaryPatch<string, string?, string?>? Settings = null,
    SetPatch<string, string>? Tags = null) : TagsPatch(Id, Tags);
