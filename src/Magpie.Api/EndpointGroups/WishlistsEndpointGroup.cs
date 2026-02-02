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

internal sealed class WishlistsEndpointGroup(IWishlistRepository wishlistRepository) : IApiEndpointGroup
{
    public string Name => "wishlists";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAllWishlists)
            .WithName("GetAllWishlists")
            .WithTags("Wishlists")
            .Produces<Wishlist[]>()
            .RequireAuthorization();

        builder.MapGet("/{id:guid}", GetWishlistById)
            .WithName("GetWishlistById")
            .WithTags("Wishlists")
            .Produces<Wishlist>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/by-user/{userId:guid}", GetWishlistsByUser)
            .WithName("GetWishlistsByUser")
            .WithTags("Wishlists")
            .Produces<Wishlist[]>()
            .RequireAuthorization();

        builder.MapGet("/monitored", GetMonitoredWishlists)
            .WithName("GetMonitoredWishlists")
            .WithTags("Wishlists")
            .Produces<Wishlist[]>()
            .RequireAuthorization();

        builder.MapPost("", CreateWishlist)
            .WithName("CreateWishlist")
            .WithTags("Wishlists")
            .Produces<Wishlist>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapPut("/{id:guid}", UpdateWishlist)
            .WithName("UpdateWishlist")
            .WithTags("Wishlists")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapPatch("/{id:guid}", PatchWishlist)
            .WithName("PatchWishlist")
            .WithTags("Wishlists")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapDelete("/{id:guid}", DeleteWishlist)
            .WithName("DeleteWishlist")
            .WithTags("Wishlists")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private async Task<IResult> GetAllWishlists(CancellationToken cancellationToken)
    {
        var wishlists = await wishlistRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(wishlists);
    }

    private async Task<IResult> GetWishlistById(Guid id, CancellationToken cancellationToken)
    {
        var wishlist = await wishlistRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return wishlist is null ? Results.NotFound() : Results.Ok(wishlist);
    }

    private async Task<IResult> GetWishlistsByUser(Guid userId, CancellationToken cancellationToken)
    {
        var wishlists = await wishlistRepository.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(wishlists);
    }

    private async Task<IResult> GetMonitoredWishlists(CancellationToken cancellationToken)
    {
        var wishlists = await wishlistRepository.GetMonitoredAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(wishlists);
    }

    private async Task<IResult> CreateWishlist(
        HttpContext httpContext,
        Wishlist request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var wishlist = request with
        {
            Id = Guid.NewGuid(),
            LastSyncedAt = null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = currentUserId,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var created = await wishlistRepository.CreateAsync(wishlist, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"/api/wishlists/{created.Id}", created);
    }

    private async Task<IResult> UpdateWishlist(
        HttpContext httpContext,
        Guid id,
        Wishlist request,
        CancellationToken cancellationToken)
    {
        var existing = await wishlistRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return Results.NotFound();
        }

        var currentUserId = httpContext.User.GetUserId();

        var wishlist = request with
        {
            Id = id,
            LastSyncedAt = existing.LastSyncedAt,
            CreatedAt = existing.CreatedAt,
            CreatedByUserId = existing.CreatedByUserId,
            ModifiedAt = DateTime.UtcNow,
            ModifiedByUserId = currentUserId
        };

        await wishlistRepository.UpdateAsync(wishlist, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.NoContent();
    }

    private async Task<IResult> PatchWishlist(
        HttpContext httpContext,
        Guid id,
        WishlistPatch request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var patch = request with { Id = id };

        var patched = await wishlistRepository.PatchAsync(patch, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    private async Task<IResult> DeleteWishlist(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var deleted = await wishlistRepository.DeleteAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
