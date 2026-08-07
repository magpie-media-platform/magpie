using System;

using Magpie.Models.Data;

namespace Magpie.Models.Arguments;

/// <summary>
/// The create arguments for a quality tier list rank.
/// </summary>
public sealed record QualityTierListRankCreate
{
    /// <summary>
    /// <inheritdoc cref="QualityTierListRank.QualityTierId" path="/summary"/>
    /// </summary>
    public Guid QualityTierId { get; init; }

    /// <summary>
    /// <inheritdoc cref="QualityTierListRank.Ordinal" path="/summary"/>
    /// </summary>
    public int Ordinal { get; init; }

    /// <summary>
    /// <inheritdoc cref="QualityTierListRank.IsCutoff" path="/summary"/>
    /// </summary>
    public bool IsCutoff { get; init; }
}
