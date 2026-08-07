using System;

using Magpie.Models.Enums;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for an indexer connection.
/// </summary>
public sealed record IndexerConnectionPatch : TagsPatch
{
    /// <summary>
    /// The patch for the display name of the connection.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The patch for whether the connection is enabled.
    /// </summary>
    public ScalarPatch<bool>? Enabled { get; init; }

    /// <summary>
    /// The patch for the connection settings.
    /// </summary>
    public DictionaryPatch<string, string?, string?>? Settings { get; init; }

    /// <summary>
    /// The patch for the connectivity status.
    /// </summary>
    public ScalarPatch<IntegrationHealthStatus>? Status { get; init; }

    /// <summary>
    /// The patch for the time of the last connectivity check.
    /// </summary>
    public ScalarPatch<DateTime?>? LastChecked { get; init; }

    /// <summary>
    /// The patch for the last error message.
    /// </summary>
    public ScalarPatch<string?>? LastError { get; init; }

    /// <summary>
    /// The patch for the count of consecutive failures.
    /// </summary>
    public ScalarPatch<int>? ConsecutiveFailures { get; init; }
}
