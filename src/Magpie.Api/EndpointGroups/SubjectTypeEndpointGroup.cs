using System.Linq;

using Magpie.Models;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class SubjectTypeEndpointGroup : IApiEndpointGroup
{
    public string Name => "subject-types";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetSubjectTypes)
            .WithName(nameof(GetSubjectTypes))
            .Produces<SubjectKindDefinition[]>()
            .RequireViewer();
    }

    internal IResult GetSubjectTypes([FromServices] ISubjectKindRegistry subjectKindRegistry)
    {
        var definitions = subjectKindRegistry.Definitions.ToArray();

        return Results.Ok(definitions);
    }
}
