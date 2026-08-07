namespace Magpie.Models.Api;

/// <summary>
/// The presence state of a secret setting whose value is withheld from read responses.
/// </summary>
public sealed record SecretSettingState
{
    /// <summary>
    /// Whether a value is stored for the key.
    /// </summary>
    public required bool IsSet { get; init; }
}
