using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for tags.
/// </summary>
public interface ITagService
{
    /// <summary>
    /// Looks up the tags that match a name prefix or that an entity carries.
    /// </summary>
    /// <param name="prefix">The prefix of the tag name to match, or <see langword="null"/> for all</param>
    /// <param name="entityId">The entity that carries the tags, or <see langword="null"/> for all</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<Tag>> LookupAsync(
        string? prefix,
        Guid? entityId,
        CancellationToken cancellationToken = default);
}
