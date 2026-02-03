using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="DownloadClient"/>.
/// </summary>
public sealed record DownloadClientPatch : TagsPatch
{
    /// <summary>
    /// The <see cref="DownloadClient.Name"/> to patch.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The <see cref="DownloadClient.Enabled"/> to patch.
    /// </summary>
    public ScalarPatch<bool>? Enabled { get; init; }

    /// <summary>
    /// The <see cref="DownloadClient.Settings"/> to patch.
    /// </summary>
    public DictionaryPatch<string, string, string>? Settings { get; init; }
}
