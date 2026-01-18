using System;

namespace Magpie.Models.Users;

/// <summary>
/// An API key for programmatic access. Permissions are inherited from the associated user.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="UserId">The user this API key belongs to.</param>
/// <param name="Name">A user-friendly name for identifying this key.</param>
/// <param name="KeyPrefix">The first few characters of the key for identification.</param>
/// <param name="Enabled">Whether the API key is enabled.</param>
/// <param name="ExpiresAt">When the API key expires, if set.</param>
/// <param name="LastUsedAt">When the API key was last used.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The user who created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The user who last modified the record.</param>
public sealed record ApiKey(
    Guid Id,
    Guid UserId,
    string Name,
    string KeyPrefix,
    bool Enabled,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? ModifiedAt,
    Guid? ModifiedByUserId);
