using System;

using Magpie.Models.Enums;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a download client.
/// </summary>
public sealed record DownloadClientPatch : TagsPatch
{
    /// <summary>
    /// The patch for the display name of the client.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The patch for whether the client is enabled.
    /// </summary>
    public ScalarPatch<bool>? Enabled { get; init; }

    /// <summary>
    /// The patch for the selection priority of the client.
    /// </summary>
    public ScalarPatch<int>? Priority { get; init; }

    /// <summary>
    /// The patch for whether cleanup deletes files from disk.
    /// </summary>
    public ScalarPatch<bool>? CleanupDeleteFiles { get; init; }

    /// <summary>
    /// The patch for whether failed downloads stay on the client.
    /// </summary>
    public ScalarPatch<bool>? KeepFailedOnClient { get; init; }

    /// <summary>
    /// The patch for the client settings.
    /// </summary>
    public DictionaryPatch<string, string, string>? Settings { get; init; }

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
