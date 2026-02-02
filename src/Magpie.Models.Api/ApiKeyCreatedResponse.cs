using System;

namespace Magpie.Models.Api;

/// <summary>
/// API response for a newly created or rotated API key (includes the plaintext key).
/// </summary>
/// <param name="Id">The API key id.</param>
/// <param name="UserId">The id of the user that owns the API key.</param>
/// <param name="Name">The display name for this API key.</param>
/// <param name="Key">The plaintext API key value. This is only returned once at creation/rotation.</param>
/// <param name="KeyPrefix">The prefix of the API key for identification.</param>
/// <param name="Enabled">Whether the API key is enabled.</param>
/// <param name="ExpiresAt">When the API key expires.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The id of the user that created the record.</param>
public sealed record ApiKeyCreatedResponse(
    Guid Id,
    Guid UserId,
    string Name,
    string Key,
    string KeyPrefix,
    bool Enabled,
    DateTime? ExpiresAt,
    DateTime CreatedAt,
    Guid? CreatedByUserId);
