namespace Magpie.Models.Users;

/// <summary>
/// An API key with its full key value. Only used for creation responses.
/// </summary>
/// <param name="ApiKey">The API key.</param>
/// <param name="Key">The full API key value. This is only returned once on creation.</param>
public sealed record ApiKeyWithKey(ApiKey ApiKey, string Key);
