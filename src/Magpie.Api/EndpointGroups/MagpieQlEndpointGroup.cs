using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Models.Filtering;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class MagpieQlEndpointGroup : IApiEndpointGroup
{
    public string Name => "magpie-ql";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapPost("content/parse", ParseContentFilter)
            .WithName(nameof(ParseContentFilter))
            .Produces<ContentFilterCriteria>()
            .Produces<MagpieQlParseError>(StatusCodes.Status400BadRequest)
            .RequireViewer();

        builder.MapPost("content/format", FormatContentFilter)
            .WithName(nameof(FormatContentFilter))
            .Produces<MagpieQlFormatResponse>()
            .RequireViewer();

        builder.MapPost("subject/parse", ParseSubjectFilter)
            .WithName(nameof(ParseSubjectFilter))
            .Produces<SubjectFilterCriteria>()
            .Produces<MagpieQlParseError>(StatusCodes.Status400BadRequest)
            .RequireViewer();

        builder.MapPost("subject/format", FormatSubjectFilter)
            .WithName(nameof(FormatSubjectFilter))
            .Produces<MagpieQlFormatResponse>()
            .RequireViewer();
    }

    internal IResult ParseContentFilter(
        [FromServices] IFilterService filterService,
        MagpieQlParseRequest request)
    {
        var result = filterService.ParseContentCriteria(request.Query);

        return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
    }

    internal IResult FormatContentFilter(
        [FromServices] IFilterService filterService,
        ContentFilterCriteria criteria)
    {
        var query = filterService.FormatContentCriteria(criteria);

        return Results.Ok(new MagpieQlFormatResponse { Query = query });
    }

    internal IResult ParseSubjectFilter(
        [FromServices] IFilterService filterService,
        MagpieQlParseRequest request)
    {
        var result = filterService.ParseSubjectCriteria(request.Query);

        return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
    }

    internal IResult FormatSubjectFilter(
        [FromServices] IFilterService filterService,
        SubjectFilterCriteria criteria)
    {
        var query = filterService.FormatSubjectCriteria(criteria);

        return Results.Ok(new MagpieQlFormatResponse { Query = query });
    }
}

