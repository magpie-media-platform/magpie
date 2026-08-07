using System.Collections.Generic;

using Magpie.Models;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class ContentTypeEndpointGroup : IApiEndpointGroup
{
    public string Name => "content-types";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetContentTypes)
            .WithName(nameof(GetContentTypes))
            .Produces<IReadOnlyList<ContentTypeDefinition>>()
            .RequireViewer();
    }

    internal IResult GetContentTypes([FromServices] IContentTypeRegistry contentTypeRegistry)
    {
        return Results.Ok(contentTypeRegistry.Definitions);
    }
}
