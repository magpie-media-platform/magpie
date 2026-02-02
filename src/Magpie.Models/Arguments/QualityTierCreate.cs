using System;

using Magpie.Models.Data;

namespace Magpie.Models.Arguments;

/// <summary>
/// Create arguments for <see cref="QualityTier"/>
/// </summary>
/// <param name="Id"><inheritdoc cref="QualityTier.Id" path="/summary"/></param>
/// <param name="Name"><inheritdoc cref="QualityTier.Name" path="/summary"/></param>
/// <param name="Criteria"><inheritdoc cref="QualityTier.Criteria" path="/summary"/></param>
public sealed record QualityTierCreate(
    Guid Id,
    string Name,
    QualityTierCriteria Criteria);
