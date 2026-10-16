using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for blocked file type rules.
/// </summary>
public interface IBlockedFileTypeRepository : IKeyedRepository<Guid, BlockedFileType>
{
    /// <summary>
    /// Creates a blocked file type rule.
    /// </summary>
    /// <param name="model">The rule to create</param>
    /// <param name="createdByUserId">
    /// The id of the user that creates the record, or <c>null</c> when no user started it.
    /// </param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The new rule</returns>
    Task<BlockedFileType> CreateAsync(
        BlockedFileType model,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a blocked file type rule by id.
    /// </summary>
    /// <param name="id">The id of the rule to delete</param>
    /// <param name="deletedByUserId">
    /// The id of the user that deletes the record, or <c>null</c> when no user started it.
    /// </param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the rule exists, otherwise <see langword="false"/></returns>
    Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken = default);
}
