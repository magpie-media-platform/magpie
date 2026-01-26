using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Data;
using Magpie.Models.Result;
using Magpie.Models.Wishlists;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using HttpResult = Microsoft.AspNetCore.Http.IResult;

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

        builder.MapGet("/by-name/{name}", GetWishlistByName)
            .WithName("GetWishlistByName")
            .WithTags("Wishlists")
            .Produces<Wishlist>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/by-user/{userId:guid}", GetWishlistsByUser)
            .WithName("GetWishlistsByUser")
            .WithTags("Wishlists")
            .Produces<Wishlist[]>()
            .RequireAuthorization();

        builder.MapGet("/by-tag/{tag}", GetWishlistsByTag)
            .WithName("GetWishlistsByTag")
            .WithTags("Wishlists")
            .Produces<Wishlist[]>()
            .RequireAuthorization();

        builder.MapGet("/by-content-filter/{contentFilterId:guid}", GetWishlistsByContentFilter)
            .WithName("GetWishlistsByContentFilter")
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

        builder.MapPatch("/{id:guid}", PatchWishlist)
            .WithName("PatchWishlist")
            .WithTags("Wishlists")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapPost("/{id:guid}/sync", UpdateLastSynced)
            .WithName("UpdateWishlistLastSynced")
            .WithTags("Wishlists")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapDelete("/{id:guid}", DeleteWishlist)
            .WithName("DeleteWishlist")
            .WithTags("Wishlists")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private async Task<HttpResult> GetAllWishlists(CancellationToken cancellationToken)
    {
        var result = await wishlistRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Wishlist>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Wishlist>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetWishlistById(Guid id, CancellationToken cancellationToken)
    {
        var result = await wishlistRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<Wishlist?> { Value: null } => Results.NotFound(),
            SuccessResult<Wishlist?> success => Results.Ok(success.Value),
            FailureResult<Wishlist?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetWishlistByName(string name, CancellationToken cancellationToken)
    {
        var result = await wishlistRepository.GetByNameAsync(name, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<Wishlist?> { Value: null } => Results.NotFound(),
            SuccessResult<Wishlist?> success => Results.Ok(success.Value),
            FailureResult<Wishlist?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetWishlistsByUser(Guid userId, CancellationToken cancellationToken)
    {
        var result = await wishlistRepository.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Wishlist>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Wishlist>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetWishlistsByTag(string tag, CancellationToken cancellationToken)
    {
        var result = await wishlistRepository.GetByTagAsync(tag, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Wishlist>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Wishlist>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetWishlistsByContentFilter(
        Guid contentFilterId,
        CancellationToken cancellationToken)
    {
        var result = await wishlistRepository.GetByContentFilterIdAsync(contentFilterId, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Wishlist>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Wishlist>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetMonitoredWishlists(CancellationToken cancellationToken)
    {
        var result = await wishlistRepository.GetMonitoredAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Wishlist>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Wishlist>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> CreateWishlist(Wishlist request, CancellationToken cancellationToken)
    {
        var wishlist = request with
        {
            Id = Guid.NewGuid(),
            LastSyncedAt = null,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = null,
            ModifiedAt = null,
            ModifiedByUserId = null
        };

        var result = await wishlistRepository.CreateAsync(wishlist, createdByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<Wishlist> success =>
                Results.Created($"/api/wishlists/{success.Value.Id}", success.Value),
            FailureResult<Wishlist> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ =>
                Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> PatchWishlist(Guid id, WishlistPatch request, CancellationToken cancellationToken)
    {
        var patch = request with { Id = id };

        var result = await wishlistRepository.PatchAsync(patch, modifiedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> { Value: false } => Results.NotFound(),
            SuccessResult<bool> => Results.NoContent(),
            FailureResult<bool> failure => Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> UpdateLastSynced(
        Guid id,
        WishlistSyncUpdate request,
        CancellationToken cancellationToken)
    {
        var result = await wishlistRepository.UpdateLastSyncedAsync(id, request.LastSyncedAt, cancellationToken)
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

    private async Task<HttpResult> DeleteWishlist(Guid id, CancellationToken cancellationToken)
    {
        var result = await wishlistRepository.DeleteAsync(id, deletedByUserId: null, cancellationToken)
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
