using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Read-only repository for querying tags.
/// </summary>
public interface ITagRepository : IRepository<Tag>
{
    /// <summary>
    /// Get all tags that start with the specified prefix.
    /// </summary>
    /// <param name="prefix">The prefix to match.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<IReadOnlyList<Tag>>> GetByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
}
