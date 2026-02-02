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
    /// The download client id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The extension that defines the download client provider.
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// The download client provider type.
    /// </summary>
    public required string ProviderType { get; set; }

    /// <summary>
    /// The display name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Whether the client is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The current health status.
    /// </summary>
    public DownloadClientStatus Status { get; set; }

    /// <summary>
    /// The date of the last health check.
    /// </summary>
    public DateTime? LastChecked { get; set; }

    /// <summary>
    /// Provider-specific settings.
    /// </summary>
    /// <remarks>
    /// The settings are stored as serialized JSON.
    /// </remarks>
    public Dictionary<string, string> Settings { get; set; } = [];

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user that created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// When the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user that last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }
}
