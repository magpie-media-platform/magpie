using System;
using System.Text.Json.Serialization;

using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for an extension.
/// </summary>
public sealed record ExtensionPatch
{
    /// <summary>
    /// The id of the extension to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for the NuGet package id.
    /// </summary>
    public ScalarPatch<string?>? NuGetId { get; init; }

    /// <summary>
    /// The patch for the package repository.
    /// </summary>
    public ScalarPatch<string?>? PackageRepository { get; init; }

    /// <summary>
    /// The patch for the stored automatic-upgrade flag.
    /// </summary>
    public ScalarPatch<bool>? AutoUpgrade { get; init; }

    /// <summary>
    /// The patch for the position in the load order.
    /// </summary>
    public ScalarPatch<int>? LoadOrder { get; init; }

    /// <summary>
    /// The patch for whether the extension is enabled.
    /// </summary>
    public ScalarPatch<bool>? Enabled { get; init; }

    /// <summary>
    /// The patch for the reason that the extension is disabled.
    /// </summary>
    [JsonIgnore]
    public ScalarPatch<string?>? DisabledReason { get; init; }
}
