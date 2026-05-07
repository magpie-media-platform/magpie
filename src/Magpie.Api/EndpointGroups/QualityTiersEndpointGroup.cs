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

internal sealed class QualityTiersEndpointGroup(
    IQualityTierRepository qualityTierRepository) : IApiEndpointGroup
{
    public string Name => "quality-tiers";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAllQualityTiers)
            .WithName(nameof(GetAllQualityTiers))
            .Produces<QualityTier[]>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetQualityTierById)
            .WithName(nameof(GetQualityTierById))
            .Produces<QualityTier>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("", CreateQualityTier)
            .WithName(nameof(CreateQualityTier))
            .Produces<QualityTier>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPatch("{id:guid}", PatchQualityTier)
            .WithName(nameof(PatchQualityTier))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapDelete("{id:guid}", DeleteQualityTier)
            .WithName(nameof(DeleteQualityTier))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();
    }

    private async Task<IResult> GetAllQualityTiers(CancellationToken cancellationToken)
    {
        var qualityTiers = await qualityTierRepository.ListAllAsync(cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(qualityTiers);
    }

    private async Task<IResult> GetQualityTierById(Guid id, CancellationToken cancellationToken)
    {
        var qualityTier = await qualityTierRepository.GetByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);

        return qualityTier is null
            ? Results.NotFound()
            : Results.Ok(qualityTier);
    }

    private async Task<IResult> CreateQualityTier(
        HttpContext httpContext,
        QualityTier request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var qualityTier = request with
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = currentUserId,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var created = await qualityTierRepository.CreateAsync(qualityTier, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"api/{Name}/{created.Id}", created);
    }

    private async Task<IResult> PatchQualityTier(
        HttpContext httpContext,
        Guid id,
        QualityTierPatch request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var patch = request with { Id = id };

        var updated = await qualityTierRepository.PatchAsync(patch, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return updated
            ? Results.NoContent()
            : Results.NotFound();
    }

    private async Task<IResult> DeleteQualityTier(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var deleted = await qualityTierRepository.DeleteAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return deleted
            ? Results.NoContent()
            : Results.NotFound();
    }
}
