using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for quality tier lists and quality tiers.
/// </summary>
public interface IQualityService
{
    /// <summary>
    /// Lists the quality tier lists.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<QualityTierList>> ListTierListsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// A quality tier list by id.
    /// </summary>
    /// <param name="id">The id of the tier list to return</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<QualityTierList?> GetTierListAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a quality tier list.
    /// </summary>
    /// <param name="tierList">The values for the new tier list</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<QualityTierListCreateResult> CreateTierListAsync(
        QualityTierList tierList,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches a quality tier list.
    /// </summary>
    /// <param name="id">The id of the tier list to patch</param>
    /// <param name="patch">The fields to change</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<QualityTierListPatchResult> PatchTierListAsync(
        Guid id,
        QualityTierListPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a quality tier list.
    /// </summary>
    /// <param name="id">The id of the tier list to delete</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteTierListAsync(Guid id, Guid? actingUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the quality tiers.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<QualityTier>> ListTiersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// A quality tier by id.
    /// </summary>
    /// <param name="id">The id of the tier to return</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<QualityTier?> GetTierAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a quality tier.
    /// </summary>
    /// <param name="tier">The values for the new tier</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<QualityTier> CreateTierAsync(
        QualityTier tier,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches a quality tier.
    /// </summary>
    /// <param name="id">The id of the tier to patch</param>
    /// <param name="patch">The fields to change</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchTierAsync(
        Guid id,
        QualityTierPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a quality tier.
    /// </summary>
    /// <param name="id">The id of the tier to delete</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteTierAsync(Guid id, Guid? actingUserId, CancellationToken cancellationToken = default);
}
