using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="IndexerConnection"/>.
/// </summary>
public sealed record IndexerConnectionPatch : TagsPatch
{
    /// <summary>
    /// The <see cref="IndexerConnection.Name"/> to patch.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The <see cref="IndexerConnection.Enabled"/> to patch.
    /// </summary>
    public ScalarPatch<bool>? Enabled { get; init; }

    /// <summary>
    /// The <see cref="IndexerConnection.Settings"/> to patch.
    /// </summary>
    public DictionaryPatch<string, string?, string?>? Settings { get; init; }
}
