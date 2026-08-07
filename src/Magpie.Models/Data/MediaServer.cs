using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// A configured connection to a media server.
/// </summary>
public record MediaServer
{
    /// <summary>
    /// The unique id of the media server.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The name of the media server to show.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The id of the extension that supplies the provider.
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// The name of the provider type that drives the connection.
    /// </summary>
    public required string ProviderType { get; set; }

    /// <summary>
    /// Whether the media server is in use.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The last known state of the connection.
    /// </summary>
    public IntegrationHealthStatus Status { get; set; }

    /// <summary>
    /// The content types that the media server holds.
    /// </summary>
    public List<string> ContentTypes { get; set; } = [];

    /// <summary>
    /// The UTC time of the last connection test.
    /// </summary>
    public DateTime? LastChecked { get; set; }

    /// <summary>
    /// The error message from the last failed connection test.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// The number of failed connection tests since the last success.
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>
    /// The provider settings, keyed by name.
    /// </summary>
    public Dictionary<string, string> Settings { get; set; } = [];

    /// <summary>
    /// The UTC time when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The UTC time when the record last changed.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user who last changed the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }
}
