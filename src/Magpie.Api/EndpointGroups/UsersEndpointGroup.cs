using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Security;
using Magpie.Core;
using Magpie.Models;
using Magpie.Models.Result;
using Magpie.Models.Users;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using HttpResult = Microsoft.AspNetCore.Http.IResult;

namespace Magpie.Api.EndpointGroups;

/// <summary>
/// Endpoint group for user management endpoints.
/// </summary>
internal sealed class UsersEndpointGroup(IUserManager userManager) : IApiEndpointGroup
{
    /// <inheritdoc />
    public string Name => "users";

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetUsers)
            .WithName("GetUsers")
            .WithTags("Users")
            .Produces<User[]>()
            .RequireAuthorization();

        builder.MapGet("/{id:guid}", GetUser)
            .WithName("GetUser")
            .WithTags("Users")
            .Produces<User>()
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

    private async Task<HttpResult> GetUsers(
        Guid? authProviderId,
        Role? role,
        bool? enabled,
        CancellationToken cancellationToken)
    {
        var result = await userManager.GetUsersAsync(authProviderId, role, enabled, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IEnumerable<User>> success =>
                Results.Ok(success.Value),
            FailureResult<IEnumerable<User>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetUser(Guid id, CancellationToken cancellationToken)
    {
        var result = await userManager.GetUserAsync(id, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<User?> { Value: null } => Results.NotFound(),
            SuccessResult<User?> success => Results.Ok(success.Value),
            FailureResult<User?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> EnableUser(Guid id, CancellationToken cancellationToken)
    {
        var patch = new UserPatch { Id = id, Enabled = new FieldPatch<bool>(true) };
        var result = await userManager.UpdateUserAsync(patch, Guid.Empty, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<User> success => Results.Ok(success.Value),
            FailureResult<User> { ErrorCode: ErrorCodes.UserNotFound } => Results.NotFound(),
            FailureResult<User> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> DisableUser(Guid id, CancellationToken cancellationToken)
    {
        var patch = new UserPatch { Id = id, Enabled = new FieldPatch<bool>(false) };
        var result = await userManager.UpdateUserAsync(patch, Guid.Empty, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<User> success => Results.Ok(success.Value),
            FailureResult<User> { ErrorCode: ErrorCodes.UserNotFound } => Results.NotFound(),
            FailureResult<User> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> DeleteUser(Guid id, CancellationToken cancellationToken)
    {
        var result = await userManager.DeleteUserAsync(id, deletedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> =>
                Results.NoContent(),
            FailureResult<bool> { ErrorCode: ErrorCodes.UserNotFound } =>
                Results.NotFound(),
            FailureResult<bool> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ =>
                Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
