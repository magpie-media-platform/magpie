using Magpie.Models.Enums;
using Magpie.Models.Patch;
using Magpie.Models.Releases;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a <see cref="ContentRelease"/>.
/// </summary>
public sealed record ContentReleasePatch
{
    /// <summary>
    /// The patch for <see cref="ContentRelease.Status"/>.
    /// </summary>
    public ScalarPatch<ReleaseScopeStatus>? Status { get; init; }
}
