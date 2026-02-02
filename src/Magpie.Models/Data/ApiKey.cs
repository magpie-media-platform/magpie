using System;

namespace Magpie.Models.Data;

/// <summary>
/// An API key for authenticating API requests.
/// </summary>
public record ApiKey
{
    /// <summary>
    /// The API key id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the user that the API key belongs to.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The display name for this API key.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The hashed API key value.
    /// </summary>
    public string? KeyHash { get; set; }

    /// <summary>
    /// The prefix of the API key for identification purposes.
    /// </summary>
    public required string KeyPrefix { get; set; }

    /// <summary>
    /// Whether the API key is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// When the API key expires.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// When the API key was last used.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

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
