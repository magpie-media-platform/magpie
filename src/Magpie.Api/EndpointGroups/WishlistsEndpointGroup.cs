using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class WishlistsEndpointGroup : IApiEndpointGroup
{
    public string Name => "wishlists";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetWishlistsAsync)
            .WithName(nameof(GetWishlistsAsync))
            .Produces<IReadOnlyList<Wishlist>>()
            .RequireViewer();

        builder.MapPost("", CreateWishlistAsync)
            .WithName(nameof(CreateWishlistAsync))
            .Produces<Wishlist>(StatusCodes.Status201Created)
            .RequireRequester();

        builder.MapGet("my", GetMyWishlistsAsync)
            .WithName(nameof(GetMyWishlistsAsync))
            .Produces<IReadOnlyList<Wishlist>>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetWishlistAsync)
            .WithName(nameof(GetWishlistAsync))
            .Produces<Wishlist>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("{id:guid}", PatchWishlistAsync)
            .WithName(nameof(PatchWishlistAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();

        builder.MapDelete("{id:guid}", DeleteWishlistAsync)
            .WithName(nameof(DeleteWishlistAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();

        builder.MapGet("{id:guid}/discovered", GetWishlistDiscoveredAsync)
            .WithName(nameof(GetWishlistDiscoveredAsync))
            .Produces<WishlistDiscoveredLedger>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();
    }

    internal async Task<IResult> GetWishlistsAsync(
        [FromServices] IWishlistService wishlistService,
        AuthenticatedUser user,
        bool? monitored,
        CancellationToken cancellationToken)
    {
        var wishlists = await wishlistService.ListVisibleAsync(monitored, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(wishlists);
    }

    internal async Task<IResult> CreateWishlistAsync(
        [FromServices] IWishlistService wishlistService,
        AuthenticatedUser user,
        WishlistCreate create,
        CancellationToken cancellationToken)
    {
        var created = await wishlistService.CreateAsync(create, user.Id, cancellationToken).ConfigureAwait(false);

        return Results.Created($"/api/{Name}/{created.Id}", created);
    }

    internal async Task<IResult> GetMyWishlistsAsync(
        [FromServices] IWishlistService wishlistService,
        AuthenticatedUser user,
        CancellationToken cancellationToken)
    {
        var wishlists = await wishlistService.ListOwnAsync(user.Id, cancellationToken).ConfigureAwait(false);

        return Results.Ok(wishlists);
    }

    internal async Task<IResult> GetWishlistAsync(
        [FromServices] IWishlistService wishlistService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var wishlist = await wishlistService.GetAsync(id, user.Id, user.IsInRole(Role.Admin), cancellationToken)
            .ConfigureAwait(false);

        return wishlist is null ? Results.NotFound() : Results.Ok(wishlist);
    }

    internal async Task<IResult> PatchWishlistAsync(
        [FromServices] IWishlistService wishlistService,
        AuthenticatedUser user,
        Guid id,
        WishlistPatch patch,
        CancellationToken cancellationToken)
    {
        var patched = await wishlistService.PatchAsync(id, patch, user.Id, user.IsInRole(Role.Admin), cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> DeleteWishlistAsync(
        [FromServices] IWishlistService wishlistService,
        AuthenticatedUser user,
        Guid id,
        bool? deleteFilter,
        CancellationToken cancellationToken)
    {
        var deleted = await wishlistService.DeleteAsync(
            id,
            deleteFilter ?? false,
            user.Id,
            user.IsInRole(Role.Admin),
            cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> GetWishlistDiscoveredAsync(
        [FromServices] IWishlistService wishlistService,
        AuthenticatedUser user,
        Guid id,
        int? pageNumber,
        int? pageSize,
        bool? inLibrary,
        WishlistDiscoveredItemStatus? status,
        WishlistDiscoveredSort? sort,
        bool? sortDescending,
        string? search,
        CancellationToken cancellationToken)
    {
        var query = new WishlistDiscoveredQuery
        {
            Paging = new PagingParameter
            {
                PageNumber = pageNumber ?? 1,
                PageSize = pageSize ?? 25,
            },
            InLibrary = inLibrary,
            Status = status,
            Sort = sort ?? WishlistDiscoveredSort.Discovered,
            SortDescending = sortDescending,
            TitleSearch = search,
        };

        var ledger = await wishlistService.GetDiscoveredAsync(
            id,
            query,
            user.Id,
            user.IsInRole(Role.Admin),
            cancellationToken)
            .ConfigureAwait(false);

        return ledger is null ? Results.NotFound() : Results.Ok(ledger);
    }
}
