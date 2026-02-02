namespace Magpie.Models.Data;

/// <summary>
/// The result of creating or rotating an API key, including the plaintext key.
/// </summary>
/// <param name="ApiKey">The API key record.</param>
/// <param name="PlaintextKey">The plaintext API key value. This is only available once.</param>
public sealed record ApiKeyWithPlaintextKey(ApiKey ApiKey, string PlaintextKey);
