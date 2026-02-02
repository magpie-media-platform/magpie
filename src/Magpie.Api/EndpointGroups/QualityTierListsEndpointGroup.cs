using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Models.Arguments;
using Magpie.Models.Data;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class QualityTierListsEndpointGroup(
    IQualityTierListRepository qualityTierListRepository) : IApiEndpointGroup
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

        builder.MapGet("/by-tag/{tagName}", GetQualityTierListsByTag)
            .WithName("GetQualityTierListsByTag")
            .WithTags("QualityTierLists")
            .Produces<QualityTierList[]>()
            .RequireAuthorization();

        builder.MapPost("", CreateQualityTierList)
            .WithName("CreateQualityTierList")
            .WithTags("QualityTierLists")
            .Produces<QualityTierList>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapPatch("/{id:guid}", PatchQualityTierList)
            .WithName("PatchQualityTierList")
            .WithTags("QualityTierLists")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapDelete("/{id:guid}", DeleteQualityTierList)
            .WithName("DeleteQualityTierList")
            .WithTags("QualityTierLists")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private async Task<IResult> GetAllQualityTierLists(CancellationToken cancellationToken)
    {
        var qualityTierLists = await qualityTierListRepository.ListAllAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(qualityTierLists);
    }

    private async Task<IResult> GetQualityTierListById(Guid id, CancellationToken cancellationToken)
    {
        var qualityTierList = await qualityTierListRepository.GetByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);

        return qualityTierList is null
            ? Results.NotFound()
            : Results.Ok(qualityTierList);
    }

    private async Task<IResult> GetQualityTierListsByTag(string tagName, CancellationToken cancellationToken)
    {
        var qualityTierLists = await qualityTierListRepository.ListByTagAsync(tagName, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(qualityTierLists);
    }

    private async Task<IResult> CreateQualityTierList(
        HttpContext httpContext,
        QualityTierList request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var qualityTierList = request with
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = currentUserId,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var created = await qualityTierListRepository.CreateAsync(qualityTierList, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"/api/quality-tier-lists/{created.Id}", created);
    }

    private async Task<IResult> PatchQualityTierList(
        HttpContext httpContext,
        Guid id,
        QualityTierListPatch request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var patch = request with { Id = id };

        var updated = await qualityTierListRepository.PatchAsync(patch, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return updated
            ? Results.NoContent()
            : Results.NotFound();
    }

    private async Task<IResult> DeleteQualityTierList(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var deleted = await qualityTierListRepository.DeleteAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return deleted
            ? Results.NoContent()
            : Results.NotFound();
    }
}
