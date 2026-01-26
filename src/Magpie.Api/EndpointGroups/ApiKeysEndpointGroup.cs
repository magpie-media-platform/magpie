using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Security;
using Magpie.Core;
using Magpie.Models.Result;
using Magpie.Models.Users;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using HttpResult = Microsoft.AspNetCore.Http.IResult;

namespace Magpie.Api.EndpointGroups;

internal sealed class ApiKeysEndpointGroup(IApiKeyManager apiKeyManager) : IApiEndpointGroup
{
    public string Name => "api-keys";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetApiKeys)
            .WithName("GetApiKeys")
            .WithTags("ApiKeys")
            .Produces<ApiKey[]>()
            .RequireAuthorization();

        builder.MapPost("", CreateApiKey)
            .WithName("CreateApiKey")
            .WithTags("ApiKeys")
            .Produces<ApiKeyWithKey>(StatusCodes.Status201Created)
            .RequireAuthorization();

        builder.MapGet("/{id:guid}", GetApiKey)
            .WithName("GetApiKey")
            .WithTags("ApiKeys")
            .Produces<ApiKey>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapPost("/{id:guid}/rotate", RotateApiKey)
            .WithName("RotateApiKey")
            .WithTags("ApiKeys")
            .Produces<ApiKeyWithKey>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapPost("/{id:guid}/enable", EnableApiKey)
            .WithName("EnableApiKey")
            .WithTags("ApiKeys")
            .Produces<ApiKey>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapPost("/{id:guid}/disable", DisableApiKey)
            .WithName("DisableApiKey")
            .WithTags("ApiKeys")
            .Produces<ApiKey>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapDelete("/{id:guid}", DeleteApiKey)
            .WithName("DeleteApiKey")
            .WithTags("ApiKeys")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private async Task<HttpResult> GetApiKeys(
        Guid? userId,
        bool? enabled,
        bool? expired,
        CancellationToken cancellationToken)
    {
        var result = await apiKeyManager.GetApiKeysAsync(userId, enabled, expired, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IEnumerable<ApiKey>> success =>
                Results.Ok(success.Value),
            FailureResult<IEnumerable<ApiKey>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> CreateApiKey(
        HttpContext httpContext,
        CreateApiKeyRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var result = await apiKeyManager.CreateApiKeyAsync(
            request.UserId,
            request.Name,
            request.ExpiresAt,
            createdByUserId: currentUserId,
            cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<ApiKeyWithKey> success =>
                Results.Created($"/{Name}/{success.Value.ApiKey.Id}", success.Value),
            FailureResult<ApiKeyWithKey> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetApiKey(Guid id, CancellationToken cancellationToken)
    {
        var result = await apiKeyManager.GetApiKeyAsync(id, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<ApiKey?> { Value: null } => Results.NotFound(),
            SuccessResult<ApiKey?> success => Results.Ok(success.Value),
            FailureResult<ApiKey?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> RotateApiKey(HttpContext httpContext, Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var result = await apiKeyManager.RotateApiKeyAsync(id, modifiedByUserId: currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<ApiKeyWithKey> success => Results.Ok(success.Value),
            FailureResult<ApiKeyWithKey> { ErrorCode: ErrorCodes.ApiKeyNotFound } => Results.NotFound(),
            FailureResult<ApiKeyWithKey> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> EnableApiKey(HttpContext httpContext, Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var result = await apiKeyManager.EnableApiKeyAsync(id, modifiedByUserId: currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<ApiKey> success => Results.Ok(success.Value),
            FailureResult<ApiKey> { ErrorCode: ErrorCodes.ApiKeyNotFound } => Results.NotFound(),
            FailureResult<ApiKey> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> DisableApiKey(HttpContext httpContext, Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var result = await apiKeyManager.DisableApiKeyAsync(id, modifiedByUserId: currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<ApiKey> success => Results.Ok(success.Value),
            FailureResult<ApiKey> { ErrorCode: ErrorCodes.ApiKeyNotFound } => Results.NotFound(),
            FailureResult<ApiKey> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> DeleteApiKey(HttpContext httpContext, Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var result = await apiKeyManager.DeleteApiKeyAsync(id, deletedByUserId: currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> =>
                Results.NoContent(),
            FailureResult<bool> { ErrorCode: ErrorCodes.ApiKeyNotFound } =>
                Results.NotFound(),
            FailureResult<bool> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ =>
                Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

}
