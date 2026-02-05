using System;

namespace Magpie.Models.Api;

/// <summary>
/// API response for a newly created or API key.
/// </summary>
public sealed record ApiKeyCreatedResponse
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
    /// The plaintext API key value.
    /// </summary>
    public required string Key { get; init; }

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
    /// When the record was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// The id of the user that created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; init; }
}

