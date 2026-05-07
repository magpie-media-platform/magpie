using Magpie.Models.Data;

namespace Magpie.Models.Api;

/// <summary>
/// Extension methods for converting domain models to API response models.
/// </summary>
public static class ResponseModelExtensions
{
    /// <summary>
    /// Convert an API key to a response model.
    /// </summary>
    public static ApiKeyResponse ToResponse(this ApiKey apiKey)
    {
        return new()
        {
            Id = apiKey.Id,
            UserId = apiKey.UserId,
            Name = apiKey.Name,
            KeyPrefix = apiKey.KeyPrefix,
            Enabled = apiKey.Enabled,
            ExpiresAt = apiKey.ExpiresAt,
            LastUsedAt = apiKey.LastUsedAt,
            CreatedAt = apiKey.CreatedAt,
            CreatedByUserId = apiKey.CreatedByUserId,
            ModifiedAt = apiKey.ModifiedAt,
            ModifiedByUserId = apiKey.ModifiedByUserId
        };
    }

    /// <summary>
    /// Convert a user to a response model that excludes credential fields.
    /// </summary>
    public static UserResponse ToResponse(this User user)
    {
        return new()
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            ExtensionId = user.ExtensionId,
            ProviderType = user.ProviderType,
            ExternalId = user.ExternalId,
            Settings = user.Settings,
            Enabled = user.Enabled,
            Role = user.Role,
            FailedLoginAttempts = user.FailedLoginAttempts,
            LockedUntil = user.LockedUntil,
            LastLoginAt = user.LastLoginAt,
            CreatedAt = user.CreatedAt,
            CreatedByUserId = user.CreatedByUserId,
            ModifiedAt = user.ModifiedAt,
            ModifiedByUserId = user.ModifiedByUserId
        };
    }

    /// <summary>
    /// Convert an API key with plaintext key to a response model.
    /// </summary>
    public static ApiKeyCreatedResponse ToCreatedResponse(this ApiKeyWithPlaintextKey result)
    {
        return new()
        {
            Id = result.ApiKey.Id,
            UserId = result.ApiKey.UserId,
            Name = result.ApiKey.Name,
            Key = result.PlaintextKey,
            KeyPrefix = result.ApiKey.KeyPrefix,
            Enabled = result.ApiKey.Enabled,
            ExpiresAt = result.ApiKey.ExpiresAt,
            CreatedAt = result.ApiKey.CreatedAt,
            CreatedByUserId = result.ApiKey.CreatedByUserId
        };
    }
}
