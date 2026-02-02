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
        builder.MapGet("", GetAllTags)
            .WithName("GetAllTags")
            .WithTags("Tags")
            .Produces<Tag[]>()
            .RequireAuthorization();

        builder.MapGet("/by-prefix/{prefix}", GetTagsByPrefix)
            .WithName("GetTagsByPrefix")
            .WithTags("Tags")
            .Produces<Tag[]>()
            .RequireAuthorization();

        builder.MapGet("/by-entity/{entityId:guid}", GetTagsByEntity)
            .WithName("GetTagsByEntity")
            .WithTags("Tags")
            .Produces<Tag[]>()
            .RequireAuthorization();
    }

    private async Task<IResult> GetAllTags(CancellationToken cancellationToken)
    {
        var tags = await tagRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(tags);
    }

    private async Task<IResult> GetTagsByPrefix(string prefix, CancellationToken cancellationToken)
    {
        var tags = await tagRepository.GetByPrefixAsync(prefix, cancellationToken).ConfigureAwait(false);

        return Results.Ok(tags);
    }

    private async Task<IResult> GetTagsByEntity(Guid entityId, CancellationToken cancellationToken)
    {
        var tags = await tagRepository.GetTagsByEntityAsync(entityId, cancellationToken).ConfigureAwait(false);

        return Results.Ok(tags);
    }
}
