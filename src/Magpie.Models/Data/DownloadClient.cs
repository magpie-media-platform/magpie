using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// A configured download client.
/// </summary>
public record DownloadClient
{
    /// <summary>
    /// The unique id of the download client.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the extension that supplies the download client.
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// The name of the provider type.
    /// </summary>
    public required string ProviderType { get; set; }

    /// <summary>
    /// The display name of the download client.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Whether the download client is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The priority of the download client.
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// The status of the download client.
    /// </summary>
    public IntegrationHealthStatus Status { get; set; }

    /// <summary>
    /// The UTC time of the last check.
    /// </summary>
    public DateTime? LastChecked { get; set; }

    /// <summary>
    /// The message of the last error.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// The number of consecutive failures.
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>
    /// Whether the cleanup deletes the files from disk.
    /// </summary>
    public bool CleanupDeleteFiles { get; set; } = true;

    /// <summary>
    /// Whether failed downloads stay on the download client.
    /// </summary>
    public bool KeepFailedOnClient { get; set; }

    /// <summary>
    /// The settings of the download client, keyed by name.
    /// </summary>
    public Dictionary<string, string> Settings { get; set; } = [];

    /// <summary>
    /// The UTC time the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The UTC time the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user who last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }
}
