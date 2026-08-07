using System.Linq;

using Magpie.Models;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class SubjectTypeEndpointGroup(
    ISubjectKindRegistry subjectKindRegistry) : IApiEndpointGroup
{
    public string Name => "subject-types";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetSubjectTypes)
            .WithName(nameof(GetSubjectTypes))
            .Produces<SubjectKindDefinition[]>()
            .RequireViewer();
    }

    internal IResult GetSubjectTypes()
    {
        var definitions = subjectKindRegistry.Definitions.ToArray();

        return Results.Ok(definitions);
    }
}
