using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class UsersEndpointGroup(IUserRepository userRepository) : IApiEndpointGroup
{
    public string Name => "users";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetUsers)
            .WithName("GetUsers")
            .WithTags("Users")
            .Produces<PagedList<User>>()
            .RequireAuthorization();

        builder.MapGet("/{id:guid}", GetUser)
            .WithName("GetUser")
            .WithTags("Users")
            .Produces<User>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapPatch("/{id:guid}", PatchUser)
            .WithName("PatchUser")
            .WithTags("Users")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapPost("/{id:guid}/enable", EnableUser)
            .WithName("EnableUser")
            .WithTags("Users")
            .Produces<User>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapPost("/{id:guid}/disable", DisableUser)
            .WithName("DisableUser")
            .WithTags("Users")
            .Produces<User>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapDelete("/{id:guid}", DeleteUser)
            .WithName("DeleteUser")
            .WithTags("Users")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private async Task<IResult> GetUsers(
        string? providerType,
        Role? role,
        bool? enabled,
        int? page,
        int? pageSize,
        string? sortBy,
        SortDirection? sortDirection,
        CancellationToken cancellationToken)
    {
        var query = new UserQuery { Enabled = enabled, Role = role, ProviderType = providerType };
        var paging = page.HasValue || pageSize.HasValue
            ? new PagingParameter { PageNumber = page ?? 1, PageSize = pageSize ?? 25 }
            : null;
        var sort = !string.IsNullOrEmpty(sortBy)
            ? new SortParameter { PropertyName = sortBy, Direction = sortDirection ?? SortDirection.Ascending }
            : null;

        var result = await userRepository.QueryAsync(query, paging, sort, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private async Task<IResult> GetUser(Guid id, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return user is null
            ? Results.NotFound()
            : Results.Ok(user);
    }

    private async Task<IResult> PatchUser(
        HttpContext httpContext,
        Guid id,
        UserPatch request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var patch = request with { Id = id };
        var updated = await userRepository.PatchAsync(patch, currentUserId, cancellationToken).ConfigureAwait(false);

        return updated
            ? Results.NoContent()
            : Results.NotFound();
    }

    private async Task<IResult> EnableUser(HttpContext httpContext, Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var existingUser = await userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (existingUser is null)
        {
            return Results.NotFound();
        }

        var patch = new UserPatch { Id = id, Enabled = new() { Value = true } };
        await userRepository.PatchAsync(patch, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        var user = await userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return Results.Ok(user);
    }

    private async Task<IResult> DisableUser(HttpContext httpContext, Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var existingUser = await userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (existingUser is null)
        {
            return Results.NotFound();
        }

        var patch = new UserPatch { Id = id, Enabled = new() { Value = false } };
        await userRepository.PatchAsync(patch, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        var user = await userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return Results.Ok(user);
    }

    private async Task<IResult> DeleteUser(HttpContext httpContext, Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var deleted = await userRepository.DeleteAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return deleted
            ? Results.NoContent()
            : Results.NotFound();
    }
}
