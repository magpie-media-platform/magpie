using System.Collections.Generic;
using System.Linq;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Content;
using Magpie.Models;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class ContentTypeEndpointGroup(
    IEnumerable<IContentTypeDefinitionProvider> contentTypeProviders) : IApiEndpointGroup
{
    public string Name => "content-types";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetContentTypes)
            .WithName(nameof(GetContentTypes))
            .Produces<ContentTypeDefinition[]>()
            .RequireViewer();
    }

    private IResult GetContentTypes()
    {
        var definitions = contentTypeProviders
            .SelectMany(p => p.GetContentTypeDefinitions())
            .ToArray();

        return Results.Ok(definitions);
    }
}
