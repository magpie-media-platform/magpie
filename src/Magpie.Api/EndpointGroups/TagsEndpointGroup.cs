using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Repositories;
using Magpie.Models.Data;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class TagsEndpointGroup(ITagRepository tagRepository) : IApiEndpointGroup
{
    public string Name => "tags";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetTags)
            .WithName(nameof(GetTags))
            .Produces<Tag[]>()
            .RequireViewer();
    }

    private async Task<IResult> GetTags(string? prefix, Guid? entityId, CancellationToken cancellationToken)
    {
        if (prefix is not null)
        {
            var tagsByPrefix = await tagRepository.GetByPrefixAsync(prefix, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(tagsByPrefix);
        }

        if (entityId is not null)
        {
            var tagsByEntity = await tagRepository.GetTagsByEntityAsync(entityId.Value, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(tagsByEntity);
        }

        var tags = await tagRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(tags);
    }
}
