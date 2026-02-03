using System;

using Magpie.Models.Data;

namespace Magpie.Models.Arguments;

/// <summary>
/// Create arguments for <see cref="QualityTier"/>
/// </summary>
public sealed record QualityTierCreate
{
    /// <summary>
    /// <inheritdoc cref="QualityTier.Id" path="/summary"/>
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// <inheritdoc cref="QualityTier.Name" path="/summary"/>
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// <inheritdoc cref="QualityTier.Criteria" path="/summary"/>
    /// </summary>
    public required string Criteria { get; init; }
}
