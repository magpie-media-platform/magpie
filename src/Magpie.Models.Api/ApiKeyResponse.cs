using System;

namespace Magpie.Models.Api;

/// <summary>
/// API response for an API key (excludes sensitive fields).
/// </summary>
/// <param name="Id">The API key id.</param>
/// <param name="UserId">The id of the user that owns the API key.</param>
/// <param name="Name">The display name for this API key.</param>
/// <param name="KeyPrefix">The prefix of the API key for identification.</param>
/// <param name="Enabled">Whether the API key is enabled.</param>
/// <param name="ExpiresAt">When the API key expires.</param>
/// <param name="LastUsedAt">When the API key was last used.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The id of the user that created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The id of the user that last modified the record.</param>
public sealed record ApiKeyResponse(
    Guid Id,
    Guid UserId,
    string Name,
    string KeyPrefix,
    bool Enabled,
    DateTime? ExpiresAt,
    DateTime? LastUsedAt,
    DateTime CreatedAt,
    Guid? CreatedByUserId,
    DateTime? ModifiedAt,
    Guid? ModifiedByUserId);
