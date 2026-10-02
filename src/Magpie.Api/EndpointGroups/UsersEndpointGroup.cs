using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;
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

[MagpieCommandGroup("users", "users", "Manage users")]
internal sealed class UsersEndpointGroup : IApiEndpointGroup
{
    public string Name => "users";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetUsersAsync)
            .WithName(nameof(GetUsersAsync))
            .Produces<PagedList<UserResponse>>()
            .RequireAdmin();

        builder.MapPost("", CreateUserAsync)
            .WithName(nameof(CreateUserAsync))
            .Produces<UserResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAdmin();

        builder.MapGet("my", GetMyUserAsync)
            .WithName(nameof(GetMyUserAsync))
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("my", PatchMyUserAsync)
            .WithName(nameof(PatchMyUserAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapGet("{id:guid}", GetUserAsync)
            .WithName(nameof(GetUserAsync))
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapPatch("{id:guid}", PatchUserAsync)
            .WithName(nameof(PatchUserAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapDelete("{id:guid}", DeleteUserAsync)
            .WithName(nameof(DeleteUserAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapGet("{id:guid}/api-keys", GetUserApiKeysAsync)
            .WithName(nameof(GetUserApiKeysAsync))
            .Produces<IReadOnlyList<ApiKeyResponse>>()
            .RequireAdmin();

        builder.MapPost("{id:guid}/password", ChangeUserPasswordAsync)
            .WithName(nameof(ChangeUserPasswordAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("{id:guid}/unlock", UnlockUserAsync)
            .WithName(nameof(UnlockUserAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();
    }

    [MagpieCommand("list", "GET", "", "List users")]
    internal async Task<IResult> GetUsersAsync(
        [FromServices] IUserService userService,
        string? providerType,
        Role? role,
        bool? enabled,
        int? pageNumber,
        int? pageSize,
        string? sortBy,
        SortDirection? sortDirection,
        CancellationToken cancellationToken)
    {
        var query = new UserQuery { Enabled = enabled, Role = role, ProviderType = providerType };
        var paging = new PagingParameter { PageNumber = pageNumber ?? 1, PageSize = pageSize ?? 25 };
        var sort = string.IsNullOrEmpty(sortBy)
            ? null
            : new SortParameter { PropertyName = sortBy, Direction = sortDirection ?? SortDirection.Ascending };

        var users = await userService.QueryAsync(query, paging, sort, cancellationToken).ConfigureAwait(false);

        return Results.Ok(CreateUsersResponse(users));
    }

    [MagpieCommand("create", "POST", "", "Create a user")]
    internal async Task<IResult> CreateUserAsync(
        [FromServices] IUserService userService,
        AuthenticatedUser user,
        [FromBody] LocalUserCreate create,
        CancellationToken cancellationToken)
    {
        var created = await userService.CreateAsync(create, user.Id, cancellationToken).ConfigureAwait(false);

        return created is null
            ? Results.Problem(
                detail: $"A user with the username '{create.Username.Trim()}' already exists.",
                statusCode: StatusCodes.Status409Conflict)
            : Results.Created($"/api/{Name}/{created.Id}", created.ToResponse());
    }

    internal async Task<IResult> GetMyUserAsync(
        [FromServices] IUserService userService,
        AuthenticatedUser user,
        CancellationToken cancellationToken)
    {
        return await userService.GetProfileAsync(user.Id, cancellationToken).ConfigureAwait(false) is { } userProfile
            ? Results.Ok(userProfile.ToResponse())
            : Results.NotFound();
    }

    internal async Task<IResult> PatchMyUserAsync(
        [FromServices] IUserService userService,
        AuthenticatedUser user,
        [FromBody] UserPatch patch,
        CancellationToken cancellationToken)
    {
        var updated = await userService.UpdateProfileAsync(patch, user.Id, cancellationToken).ConfigureAwait(false);

        return updated ? Results.NoContent() : Results.NotFound();
    }

    [MagpieCommand("get", "GET", "{id:guid}", "Show a user")]
    internal async Task<IResult> GetUserAsync(
        [FromServices] IUserService userService,
        Guid id,
        CancellationToken cancellationToken)
    {
        return await userService.GetAsync(id, cancellationToken).ConfigureAwait(false) is { } user
            ? Results.Ok(user.ToResponse())
            : Results.NotFound();
    }

    [MagpieCommand("patch", "PATCH", "{id:guid}", "Update fields on a user")]
    internal async Task<IResult> PatchUserAsync(
        [FromServices] IUserService userService,
        AuthenticatedUser user,
        Guid id,
        [FromBody] UserPatch patch,
        CancellationToken cancellationToken)
    {
        return await userService.PatchAsync(id, patch, user.Id, cancellationToken).ConfigureAwait(false)
            ? Results.NoContent()
            : Results.NotFound();
    }

    [MagpieCommand("delete", "DELETE", "{id:guid}", "Delete a user")]
    internal async Task<IResult> DeleteUserAsync(
        [FromServices] IUserService userService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        return await userService.DeleteAsync(id, user.Id, cancellationToken).ConfigureAwait(false)
            ? Results.NoContent()
            : Results.NotFound();
    }

    internal async Task<IResult> GetUserApiKeysAsync(
        [FromServices] IApiKeyService apiKeyService,
        Guid id,
        bool? enabled,
        bool? expired,
        CancellationToken cancellationToken)
    {
        var apiKeys = await apiKeyService.ListForUserAsync(id, enabled, expired, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(ApiKeysEndpointGroup.CreateResponse(apiKeys));
    }

    internal async Task<IResult> ChangeUserPasswordAsync(
        [FromServices] IUserService userService,
        AuthenticatedUser user,
        Guid id,
        PasswordChange change,
        CancellationToken cancellationToken)
    {
        var changed = await userService.ChangePasswordAsync(
            id,
            change,
            user.Id,
            user.IsInRole(Role.Admin),
            cancellationToken)
            .ConfigureAwait(false);

        return changed ? Results.NoContent() : Results.NotFound();
    }

    [MagpieCommand("unlock", "POST", "{id:guid}/unlock", "Clear the lockout on a user")]
    internal async Task<IResult> UnlockUserAsync(
        [FromServices] IUserService userService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        return await userService.UnlockAsync(id, user.Id, cancellationToken).ConfigureAwait(false)
            ? Results.NoContent()
            : Results.NotFound();
    }

    private static PagedList<UserResponse> CreateUsersResponse(PagedList<User> users)
    {
        return new()
        {
            Items = [.. users.Items.Select(UserExtensions.ToResponse)],
            PageNumber = users.PageNumber,
            PageSize = users.PageSize,
            TotalItems = users.TotalItems,
        };
    }
}
