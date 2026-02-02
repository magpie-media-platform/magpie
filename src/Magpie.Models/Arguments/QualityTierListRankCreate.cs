using System;

using Magpie.Models.Data;

namespace Magpie.Models.Arguments;

/// <summary>
/// Create arguments for <see cref="QualityTierListRank"/>
/// </summary>
/// <param name="QualityTierId"><inheritdoc cref="QualityTierListRank.QualityTierId" path="/summary"/></param>
/// <param name="Ordinal"><inheritdoc cref="QualityTierListRank.Ordinal" path="/summary"/></param>
/// <param name="IsCutoff"><inheritdoc cref="QualityTierListRank.IsCutoff" path="/summary"/></param>
public sealed record QualityTierListRankCreate(Guid QualityTierId, int Ordinal, bool IsCutoff);
