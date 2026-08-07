using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class TagsEndpointGroup : IApiEndpointGroup
{
    public string Name => "tags";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetTagsAsync)
            .WithName(nameof(GetTagsAsync))
            .Produces<IReadOnlyList<Tag>>()
            .RequireViewer();
    }

    internal async Task<IResult> GetTagsAsync(
        [FromServices] ITagService tagService,
        string? prefix,
        Guid? entityId,
        CancellationToken cancellationToken)
    {
        var tags = await tagService.LookupAsync(prefix, entityId, cancellationToken).ConfigureAwait(false);

        return Results.Ok(tags);
    }
}
