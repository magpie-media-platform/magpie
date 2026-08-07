using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// A configured connection to an indexer.
/// </summary>
public record IndexerConnection
{
    /// <summary>
    /// The unique id of the indexer connection.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the indexer.
    /// </summary>
    public Guid IndexerId { get; set; }

    /// <summary>
    /// The display name of the connection.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Whether the connection is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The status of the connection.
    /// </summary>
    public IntegrationHealthStatus Status { get; set; }

    /// <summary>
    /// The settings of the connection, keyed by name.
    /// </summary>
    public Dictionary<string, string> Settings { get; set; } = [];

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
