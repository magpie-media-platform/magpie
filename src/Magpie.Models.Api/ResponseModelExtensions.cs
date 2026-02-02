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
    public static ApiKeyResponse ToResponse(this ApiKey apiKey) => new(
        apiKey.Id,
        apiKey.UserId,
        apiKey.Name,
        apiKey.KeyPrefix,
        apiKey.Enabled,
        apiKey.ExpiresAt,
        apiKey.LastUsedAt,
        apiKey.CreatedAt,
        apiKey.CreatedByUserId,
        apiKey.ModifiedAt,
        apiKey.ModifiedByUserId);

    /// <summary>
    /// Convert an API key with plaintext key to a response model.
    /// </summary>
    public static ApiKeyCreatedResponse ToCreatedResponse(this ApiKeyWithPlaintextKey result) => new(
        result.ApiKey.Id,
        result.ApiKey.UserId,
        result.ApiKey.Name,
        result.PlaintextKey,
        result.ApiKey.KeyPrefix,
        result.ApiKey.Enabled,
        result.ApiKey.ExpiresAt,
        result.ApiKey.CreatedAt,
        result.ApiKey.CreatedByUserId);
}
