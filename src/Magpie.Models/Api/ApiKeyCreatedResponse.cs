namespace Magpie.Models.Api;

/// <summary>
/// The response for the creation of an API key.
/// </summary>
public sealed record ApiKeyCreatedResponse
{
    /// <summary>
    /// The created API key.
    /// </summary>
    public required ApiKeyResponse Key { get; init; }

    /// <summary>
    /// The plaintext key value, available only in this response.
    /// </summary>
    public required string PlaintextKey { get; init; }
}
