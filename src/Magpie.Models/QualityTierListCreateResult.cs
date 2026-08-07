using Magpie.Models.Data;

namespace Magpie.Models;

/// <summary>
/// The result of creating a quality tier list.
/// </summary>
public sealed record QualityTierListCreateResult
{
    /// <summary>
    /// The created quality tier list, or <see langword="null"/> when validation fails.
    /// </summary>
    public QualityTierList? TierList { get; init; }

    /// <summary>
    /// The validation error, or <see langword="null"/> when creation succeeds.
    /// </summary>
    public string? ValidationError { get; init; }
}
