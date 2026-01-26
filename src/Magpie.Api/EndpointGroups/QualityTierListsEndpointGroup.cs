using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Data;
using Magpie.Models.QualityTiers;
using Magpie.Models.Result;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using HttpResult = Microsoft.AspNetCore.Http.IResult;

namespace Magpie.Api.EndpointGroups;

internal sealed class QualityTierListsEndpointGroup(IQualityTierListRepository qualityTierListRepository)
    : IApiEndpointGroup
{
    public string Name => "quality-tier-lists";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAllQualityTierLists)
            .WithName("GetAllQualityTierLists")
            .WithTags("QualityTierLists")
            .Produces<QualityTierList[]>()
            .RequireAuthorization();

        builder.MapGet("/{id:guid}", GetQualityTierListById)
            .WithName("GetQualityTierListById")
            .WithTags("QualityTierLists")
            .Produces<QualityTierList>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/by-name/{name}", GetQualityTierListByName)
            .WithName("GetQualityTierListByName")
            .WithTags("QualityTierLists")
            .Produces<QualityTierList>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapDelete("/{id:guid}", DeleteQualityTierList)
            .WithName("DeleteQualityTierList")
            .WithTags("QualityTierLists")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private async Task<HttpResult> GetAllQualityTierLists(CancellationToken cancellationToken)
    {
        var result = await qualityTierListRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<QualityTierList>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<QualityTierList>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetQualityTierListById(Guid id, CancellationToken cancellationToken)
    {
        var result = await qualityTierListRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<QualityTierList?> { Value: null } => Results.NotFound(),
            SuccessResult<QualityTierList?> success => Results.Ok(success.Value),
            FailureResult<QualityTierList?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetQualityTierListByName(string name, CancellationToken cancellationToken)
    {
        var result = await qualityTierListRepository.GetByNameAsync(name, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<QualityTierList?> { Value: null } => Results.NotFound(),
            SuccessResult<QualityTierList?> success => Results.Ok(success.Value),
            FailureResult<QualityTierList?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> DeleteQualityTierList(Guid id, CancellationToken cancellationToken)
    {
        var result = await qualityTierListRepository.DeleteAsync(id, deletedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> { Value: false } => Results.NotFound(),
            SuccessResult<bool> => Results.NoContent(),
            FailureResult<bool> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
