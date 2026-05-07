using System.Collections.Generic;
using System.Linq;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Content;
using Magpie.Models;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class SubjectTypeEndpointGroup(
    IEnumerable<ISubjectKindDefinitionProvider> subjectKindProviders) : IApiEndpointGroup
{
    public string Name => "subject-types";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetSubjectTypes)
            .WithName(nameof(GetSubjectTypes))
            .Produces<SubjectKindDefinition[]>()
            .RequireViewer();
    }

    private IResult GetSubjectTypes()
    {
        var definitions = subjectKindProviders
            .SelectMany(p => p.GetSubjectKindDefinitions())
            .ToArray();

        return Results.Ok(definitions);
    }
}
