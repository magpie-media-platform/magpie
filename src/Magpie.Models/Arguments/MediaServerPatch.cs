using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="MediaServer"/>.
/// </summary>
public sealed record MediaServerPatch : TagsPatch
{
    /// <summary>
    /// The <see cref="MediaServer.Name"/> to patch.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The <see cref="MediaServer.Enabled"/> to patch.
    /// </summary>
    public ScalarPatch<bool>? Enabled { get; init; }

    /// <summary>
    /// The <see cref="MediaServer.Settings"/> to patch.
    /// </summary>
    public DictionaryPatch<string, string?, string?>? Settings { get; init; }
}
