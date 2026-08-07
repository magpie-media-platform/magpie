using System;

namespace Magpie.Models.Data;

/// <summary>
/// An API key that authenticates API requests.
/// </summary>
public record ApiKey
{
    /// <summary>
    /// The unique id of the API key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the user who owns the API key.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The display name of the API key.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The hash of the API key value.
    /// </summary>
    public string? KeyHash { get; set; }

    /// <summary>
    /// The visible prefix that identifies the API key.
    /// </summary>
    public required string KeyPrefix { get; set; }

    /// <summary>
    /// Whether the API key is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The UTC time the API key expires.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// The UTC time of the last use of the API key.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

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
