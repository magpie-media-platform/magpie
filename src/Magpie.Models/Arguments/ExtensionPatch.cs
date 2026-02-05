using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="Extension"/>.
/// </summary>
public sealed record ExtensionPatch
{
    /// <summary>
    /// The id of the extension to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The <see cref="Extension.NuGetId"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? NuGetId { get; init; }

    /// <summary>
    /// The <see cref="Extension.PackageRepository"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? PackageRepository { get; init; }

    /// <summary>
    /// The <see cref="Extension.AutoUpgrade"/> to patch.
    /// </summary>
    public ScalarPatch<bool>? AutoUpgrade { get; init; }
}
