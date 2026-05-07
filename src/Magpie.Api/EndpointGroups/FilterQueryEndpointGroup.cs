using Magpie.Abstractions.Api;
using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.QueryLanguage;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class FilterQueryEndpointGroup : IApiEndpointGroup
{
    public string Name => "filter-query";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapPost("content/parse", ParseContentFilter)
            .WithName(nameof(ParseContentFilter))
            .Produces<ContentFilterCriteria>()
            .Produces<FilterQueryErrorResponse>(StatusCodes.Status400BadRequest)
            .RequireViewer();

        builder.MapPost("content/format", FormatContentFilter)
            .WithName(nameof(FormatContentFilter))
            .Produces<FilterQueryFormatResponse>()
            .RequireViewer();

        builder.MapPost("subject/parse", ParseSubjectFilter)
            .WithName(nameof(ParseSubjectFilter))
            .Produces<SubjectFilterCriteria>()
            .Produces<FilterQueryErrorResponse>(StatusCodes.Status400BadRequest)
            .RequireViewer();

        builder.MapPost("subject/format", FormatSubjectFilter)
            .WithName(nameof(FormatSubjectFilter))
            .Produces<FilterQueryFormatResponse>()
            .RequireViewer();
    }

    private static IResult ParseContentFilter(FilterQueryParseRequest request)
    {
        var result = FilterQuery.ParseContentFilter(request.Query);
        if (!result.IsSuccess)
        {
            var error = result.Error!.Value;
            return Results.BadRequest(new FilterQueryErrorResponse
            {
                Message = error.Message,
                Position = error.Position,
                Expected = error.Expected
            });
        }

        return Results.Ok(result.Value);
    }

    private static IResult FormatContentFilter(ContentFilterCriteria criteria)
    {
        var query = FilterQuery.FormatContentFilter(criteria);
        return Results.Ok(new FilterQueryFormatResponse { Query = query });
    }

    private static IResult ParseSubjectFilter(FilterQueryParseRequest request)
    {
        var result = FilterQuery.ParseSubjectFilter(request.Query);
        if (!result.IsSuccess)
        {
            var error = result.Error!.Value;
            return Results.BadRequest(new FilterQueryErrorResponse
            {
                Message = error.Message,
                Position = error.Position,
                Expected = error.Expected
            });
        }

        return Results.Ok(result.Value);
    }

    private static IResult FormatSubjectFilter(SubjectFilterCriteria criteria)
    {
        var query = FilterQuery.FormatSubjectFilter(criteria);
        return Results.Ok(new FilterQueryFormatResponse { Query = query });
    }
}
