using System;

namespace Magpie.Models.Api;

/// <summary>
/// The API representation of an API key.
/// </summary>
public sealed record ApiKeyResponse
{
    /// <summary>
    /// The id of the API key.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The id of the user who owns the API key.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// The name of the API key.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The visible prefix of the API key.
    /// </summary>
    public required string KeyPrefix { get; init; }

    /// <summary>
    /// Whether the API key is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// The time when the API key expires.
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    /// The time of the last use of the API key.
    /// </summary>
    public DateTime? LastUsedAt { get; init; }

    /// <summary>
    /// The UTC time when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; init; }

    /// <summary>
    /// The UTC time when the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; init; }

    /// <summary>
    /// The id of the user who last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; init; }
}
