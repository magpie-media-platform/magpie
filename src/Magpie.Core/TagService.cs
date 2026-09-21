using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Services;

namespace Magpie.Core;

internal sealed class TagService(ITagRepository tags) : ITagService
{
    public async Task<IReadOnlyList<Tag>> LookupAsync(
        string? prefix,
        Guid? entityId,
        CancellationToken cancellationToken = default)
    {
        return await tags.QueryAllAsync(
            new TagQuery { Prefix = entityId is null ? prefix ?? string.Empty : null, EntityId = entityId },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
