namespace Magpie.Models.Data;

/// <summary>
/// The result of the creation or the rotation of an API key, with the plaintext key.
/// </summary>
/// <param name="ApiKey">The API key record</param>
/// <param name="PlaintextKey">The plaintext key value, which Magpie returns one time only</param>
public sealed record ApiKeyWithPlaintextKey(ApiKey ApiKey, string PlaintextKey);
