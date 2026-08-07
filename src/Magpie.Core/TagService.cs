using System;
using System.Collections.Generic;
using System.Linq;
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
        const int pageSize = 500;
        var items = new List<Tag>();
        var pageNumber = 1;
        while (true)
        {
            var page = await tags.QueryAsync(
                new TagQuery { Prefix = entityId is null ? prefix ?? string.Empty : null, EntityId = entityId },
                new() { PageNumber = pageNumber, PageSize = pageSize },
                null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            items.AddRange(pageItems);
            if (pageItems.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

        return items;
    }
}
