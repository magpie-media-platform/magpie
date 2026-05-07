using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Api.Request;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Enums;
using Magpie.Models.Paging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class UsersEndpointGroup(
    IUserRepository userRepository,
    IUserManager userManager,
    IPasswordHasher passwordHasher) : IApiEndpointGroup
{
    public string Name => "users";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("me", GetCurrentUser)
            .WithName(nameof(GetCurrentUser))
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireViewer();

        builder.MapPatch("me", PatchCurrentUser)
            .WithName(nameof(PatchCurrentUser))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapGet("", GetUsers)
            .WithName(nameof(GetUsers))
            .Produces<PagedList<UserResponse>>()
            .RequireAdmin();

        builder.MapPost("", CreateUser)
            .WithName(nameof(CreateUser))
            .Produces<UserResponse>(StatusCodes.Status201Created)
            .RequireAdmin();

        builder.MapGet("{id:guid}", GetUser)
            .WithName(nameof(GetUser))
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapPatch("{id:guid}", PatchUser)
            .WithName(nameof(PatchUser))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapPost("{id:guid}/enable", EnableUser)
            .WithName(nameof(EnableUser))
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapPost("{id:guid}/disable", DisableUser)
            .WithName(nameof(DisableUser))
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapDelete("{id:guid}", DeleteUser)
            .WithName(nameof(DeleteUser))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();
    }

    private async Task<IResult> GetCurrentUser(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var user = await userRepository.GetByIdAsync(userId.Value, cancellationToken).ConfigureAwait(false);

        return user is null
            ? Results.NotFound()
            : Results.Ok(user.ToResponse());
    }

    private async Task<IResult> PatchCurrentUser(
        HttpContext httpContext,
        UserPatch request,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var patch = request with { Id = userId.Value };
        var updated = await userRepository.PatchAsync(patch, userId.Value, cancellationToken).ConfigureAwait(false);

        return updated
            ? Results.NoContent()
            : Results.NotFound();
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

        var response = new PagedList<UserResponse>
        {
            Items = result.Items.Select(u => u.ToResponse()),
            PageNumber = result.PageNumber,
            PageSize = result.PageSize,
            TotalItems = result.TotalItems
        };

        return Results.Ok(response);
    }

    private async Task<IResult> CreateUser(
        HttpContext httpContext,
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var hashedPassword = await passwordHasher.HashPasswordAsync(request.Password, cancellationToken)
            .ConfigureAwait(false);

        var createArgs = new UserCreate
        {
            Username = request.Username,
            PasswordHash = hashedPassword.Hash,
            PasswordSalt = hashedPassword.Salt,
            Role = request.Role,
        };

        var user = await userManager.CreateLocalUserAsync(createArgs, cancellationToken).ConfigureAwait(false);

        return Results.Created($"api/{Name}/{user.Id}", user.ToResponse());
    }

    private async Task<IResult> GetUser(Guid id, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return user is null
            ? Results.NotFound()
            : Results.Ok(user.ToResponse());
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
        return Results.Ok(user?.ToResponse());
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
        return Results.Ok(user?.ToResponse());
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
