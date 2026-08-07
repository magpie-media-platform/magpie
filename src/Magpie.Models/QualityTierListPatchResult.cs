namespace Magpie.Models;

/// <summary>
/// The result of patching a quality tier list.
/// </summary>
public sealed record QualityTierListPatchResult
{
    /// <summary>
    /// Whether the quality tier list was updated.
    /// </summary>
    public bool Updated { get; init; }

    /// <summary>
    /// The validation error, or <see langword="null"/> when validation succeeds.
    /// </summary>
    public string? ValidationError { get; init; }
}
