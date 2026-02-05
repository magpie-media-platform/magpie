using System;

namespace Magpie.Models.Api;

/// <summary>
/// API response for an API key (excludes sensitive fields).
/// </summary>
public sealed record ApiKeyResponse
{
    /// <summary>
    /// The API key id.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The id of the user that owns the API key.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// The display name for this API key.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The prefix of the API key for identification.
    /// </summary>
    public required string KeyPrefix { get; init; }

    /// <summary>
    /// Whether the API key is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// When the API key expires.
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    /// When the API key was last used.
    /// </summary>
    public DateTime? LastUsedAt { get; init; }

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// The id of the user that created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; init; }

    /// <summary>
    /// When the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; init; }

    /// <summary>
    /// The id of the user that last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; init; }
}
