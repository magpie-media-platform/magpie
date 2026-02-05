using System;
using System.Linq;
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
        builder.MapGet("my", GetMyWishlists)
            .WithName(nameof(GetMyWishlists))
            .Produces<Wishlist[]>()
            .RequireViewer();

        builder.MapPost("my", CreateMyWishlist)
            .WithName(nameof(CreateMyWishlist))
            .Produces<Wishlist>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireRequester();

        builder.MapGet("my/{id:guid}", GetMyWishlist)
            .WithName(nameof(GetMyWishlist))
            .Produces<Wishlist>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("my/{id:guid}", PatchMyWishlist)
            .WithName(nameof(PatchMyWishlist))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireRequester();

        builder.MapDelete("my/{id:guid}", DeleteMyWishlist)
            .WithName(nameof(DeleteMyWishlist))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();

        builder.MapPost("my/{id:guid}/lock", LockMyWishlist)
            .WithName(nameof(LockMyWishlist))
            .Produces<Wishlist>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();

        builder.MapPost("my/{id:guid}/unlock", UnlockMyWishlist)
            .WithName(nameof(UnlockMyWishlist))
            .Produces<Wishlist>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();
        builder.MapGet("", GetWishlists)
            .WithName(nameof(GetWishlists))
            .Produces<Wishlist[]>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetWishlistById)
            .WithName(nameof(GetWishlistById))
            .Produces<Wishlist>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("{id:guid}", PatchSharedWishlist)
            .WithName(nameof(PatchSharedWishlist))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireRequester();

        builder.MapPost("{id:guid}/unlock", UnlockWishlist)
            .WithName(nameof(UnlockWishlist))
            .Produces<Wishlist>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();
        builder.MapGet("{userId:guid}/{id:guid}", GetUserWishlist)
            .WithName(nameof(GetUserWishlist))
            .Produces<Wishlist>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapDelete("{userId:guid}/{id:guid}", DeleteUserWishlist)
            .WithName(nameof(DeleteUserWishlist))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();
    }

    private async Task<IResult> GetMyWishlists(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var wishlists = await wishlistRepository.GetByUserIdAsync(userId.Value, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(wishlists.ToArray());
    }

    private async Task<IResult> CreateMyWishlist(
        HttpContext httpContext,
        Wishlist request,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var wishlist = request with
        {
            Id = Guid.NewGuid(),
            OwnerUserId = userId.Value,
            LastSyncedAt = null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var created = await wishlistRepository.CreateAsync(wishlist, userId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"api/{Name}/my/{created.Id}", created);
    }

    private async Task<IResult> GetMyWishlist(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var wishlist = await wishlistRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (wishlist is null || wishlist.OwnerUserId != userId)
        {
            return Results.NotFound();
        }

        return Results.Ok(wishlist);
    }

    private async Task<IResult> PatchMyWishlist(
        HttpContext httpContext,
        Guid id,
        WishlistPatch request,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var existing = await wishlistRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null || existing.OwnerUserId != userId)
        {
            return Results.NotFound();
        }

        var patch = request with { Id = id };

        var patched = await wishlistRepository.PatchAsync(patch, userId, cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    private async Task<IResult> DeleteMyWishlist(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var existing = await wishlistRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null || existing.OwnerUserId != userId)
        {
            return Results.NotFound();
        }

        var deleted = await wishlistRepository.DeleteAsync(id, userId, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    private async Task<IResult> LockMyWishlist(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var existing = await wishlistRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null || existing.OwnerUserId != userId)
        {
            return Results.NotFound();
        }

        var wishlist = existing with
        {
            OwnerLock = true,
            ModifiedAt = DateTime.UtcNow,
            ModifiedByUserId = userId
        };

        await wishlistRepository.UpdateAsync(wishlist, userId, cancellationToken).ConfigureAwait(false);

        return Results.Ok(wishlist);
    }

    private async Task<IResult> UnlockMyWishlist(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var existing = await wishlistRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null || existing.OwnerUserId != userId)
        {
            return Results.NotFound();
        }

        var wishlist = existing with
        {
            OwnerLock = false,
            ModifiedAt = DateTime.UtcNow,
            ModifiedByUserId = userId
        };

        await wishlistRepository.UpdateAsync(wishlist, userId, cancellationToken).ConfigureAwait(false);

        return Results.Ok(wishlist);
    }

    private async Task<IResult> GetWishlists(
        bool? monitored,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (userId.HasValue)
        {
            var userWishlists = await wishlistRepository.GetByUserIdAsync(userId.Value, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(userWishlists.ToArray());
        }

        if (monitored == true)
        {
            var monitoredWishlists = await wishlistRepository.GetMonitoredAsync(cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(monitoredWishlists.ToArray());
        }

        var wishlists = await wishlistRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(wishlists.ToArray());
    }

    private async Task<IResult> GetWishlistById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var wishlist = await wishlistRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return wishlist is null ? Results.NotFound() : Results.Ok(wishlist);
    }

    private async Task<IResult> PatchSharedWishlist(
        HttpContext httpContext,
        Guid id,
        WishlistPatch request,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var existing = await wishlistRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return Results.NotFound();
        }

        if (existing.OwnerLock && existing.OwnerUserId != userId)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
        }

        var patch = request with { Id = id };

        var patched = await wishlistRepository.PatchAsync(patch, userId, cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    private async Task<IResult> UnlockWishlist(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();

        var existing = await wishlistRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return Results.NotFound();
        }

        var wishlist = existing with
        {
            OwnerLock = false,
            ModifiedAt = DateTime.UtcNow,
            ModifiedByUserId = userId
        };

        await wishlistRepository.UpdateAsync(wishlist, userId, cancellationToken).ConfigureAwait(false);

        return Results.Ok(wishlist);
    }

    private async Task<IResult> GetUserWishlist(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var wishlist = await wishlistRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (wishlist is null || wishlist.OwnerUserId != userId)
        {
            return Results.NotFound();
        }

        return Results.Ok(wishlist);
    }

    private async Task<IResult> DeleteUserWishlist(
        HttpContext httpContext,
        Guid userId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var existing = await wishlistRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null || existing.OwnerUserId != userId)
        {
            return Results.NotFound();
        }

        var deleted = await wishlistRepository.DeleteAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
