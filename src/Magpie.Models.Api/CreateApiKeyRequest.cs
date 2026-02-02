using System;

namespace Magpie.Models.Api;

/// <summary>
/// Request to create an API key.
/// </summary>
/// <param name="UserId">The user id to associate the key with.</param>
/// <param name="Name">A user-friendly name for the key.</param>
/// <param name="ExpiresAt">When the key expires, if set.</param>
public sealed record CreateApiKeyRequest(Guid UserId, string Name, DateTime? ExpiresAt);
