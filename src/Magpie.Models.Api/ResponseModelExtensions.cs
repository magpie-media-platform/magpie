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
