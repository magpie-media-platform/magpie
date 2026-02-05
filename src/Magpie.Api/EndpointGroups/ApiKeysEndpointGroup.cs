using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Security;
using Magpie.Models.Api;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class ApiKeysEndpointGroup(IApiKeyManager apiKeyManager) : IApiEndpointGroup
{
    public string Name => "api-keys";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("my", GetMyApiKeys)
            .WithName(nameof(GetMyApiKeys))
            .Produces<ApiKeyResponse[]>()
            .RequireViewer();

        builder.MapPost("my", CreateMyApiKey)
            .WithName(nameof(CreateMyApiKey))
            .Produces<ApiKeyCreatedResponse>(StatusCodes.Status201Created)
            .RequireViewer();

        builder.MapGet("my/{id:guid}", GetMyApiKey)
            .WithName(nameof(GetMyApiKey))
            .Produces<ApiKeyResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("my/{id:guid}/rotate", RotateMyApiKey)
            .WithName(nameof(RotateMyApiKey))
            .Produces<ApiKeyCreatedResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("my/{id:guid}/enable", EnableMyApiKey)
            .WithName(nameof(EnableMyApiKey))
            .Produces<ApiKeyResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPost("my/{id:guid}/disable", DisableMyApiKey)
            .WithName(nameof(DisableMyApiKey))
            .Produces<ApiKeyResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapDelete("my/{id:guid}", DeleteMyApiKey)
            .WithName(nameof(DeleteMyApiKey))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapGet("{userId:guid}", GetUserApiKeys)
            .WithName(nameof(GetUserApiKeys))
            .Produces<ApiKeyResponse[]>()
            .RequireAdmin();

        builder.MapGet("{userId:guid}/{id:guid}", GetUserApiKey)
            .WithName(nameof(GetUserApiKey))
            .Produces<ApiKeyResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapDelete("{userId:guid}/{id:guid}", DeleteUserApiKey)
            .WithName(nameof(DeleteUserApiKey))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();
    }

    private async Task<IResult> GetMyApiKeys(
        HttpContext httpContext,
        bool? enabled,
        bool? expired,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var apiKeys = await apiKeyManager.ListApiKeysAsync(userId.Value, enabled, expired, cancellationToken)
            .ConfigureAwait(false);

        var responses = apiKeys.Select(k => k.ToResponse()).ToArray();
        return Results.Ok(responses);
    }

    private async Task<IResult> CreateMyApiKey(
        HttpContext httpContext,
        CreateMyApiKeyRequest request,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await apiKeyManager.CreateApiKeyAsync(
            userId.Value,
            request.Name,
            request.ExpiresAt,
            userId,
            cancellationToken).ConfigureAwait(false);

        return Results.Created($"{Name}/my/{result.ApiKey.Id}", result.ToCreatedResponse());
    }

    private async Task<IResult> GetMyApiKey(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var apiKey = await apiKeyManager.GetApiKeyAsync(id, cancellationToken).ConfigureAwait(false);

        if (apiKey is null || apiKey.UserId != userId)
        {
            return Results.NotFound();
        }

        return Results.Ok(apiKey.ToResponse());
    }

    private async Task<IResult> RotateMyApiKey(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var existing = await apiKeyManager.GetApiKeyAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null || existing.UserId != userId)
        {
            return Results.NotFound();
        }

        var result = await apiKeyManager.RotateApiKeyAsync(id, userId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result.ToCreatedResponse());
    }

    private async Task<IResult> EnableMyApiKey(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var existing = await apiKeyManager.GetApiKeyAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null || existing.UserId != userId)
        {
            return Results.NotFound();
        }

        var apiKey = await apiKeyManager.EnableApiKeyAsync(id, userId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(apiKey.ToResponse());
    }

    private async Task<IResult> DisableMyApiKey(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var existing = await apiKeyManager.GetApiKeyAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null || existing.UserId != userId)
        {
            return Results.NotFound();
        }

        var apiKey = await apiKeyManager.DisableApiKeyAsync(id, userId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(apiKey.ToResponse());
    }

    private async Task<IResult> DeleteMyApiKey(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();
        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var existing = await apiKeyManager.GetApiKeyAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null || existing.UserId != userId)
        {
            return Results.NotFound();
        }

        var deleted = await apiKeyManager.DeleteApiKeyAsync(id, userId, cancellationToken)
            .ConfigureAwait(false);

        if (!deleted)
        {
            return Results.NotFound();
        }

        return Results.NoContent();
    }

    private async Task<IResult> GetUserApiKeys(
        Guid userId,
        bool? enabled,
        bool? expired,
        CancellationToken cancellationToken)
    {
        var apiKeys = await apiKeyManager.ListApiKeysAsync(userId, enabled, expired, cancellationToken)
            .ConfigureAwait(false);

        var responses = apiKeys.Select(k => k.ToResponse()).ToArray();
        return Results.Ok(responses);
    }

    private async Task<IResult> GetUserApiKey(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var apiKey = await apiKeyManager.GetApiKeyAsync(id, cancellationToken).ConfigureAwait(false);

        if (apiKey is null || apiKey.UserId != userId)
        {
            return Results.NotFound();
        }

        return Results.Ok(apiKey.ToResponse());
    }

    private async Task<IResult> DeleteUserApiKey(
        HttpContext httpContext,
        Guid userId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var existing = await apiKeyManager.GetApiKeyAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null || existing.UserId != userId)
        {
            return Results.NotFound();
        }

        var deleted = await apiKeyManager.DeleteApiKeyAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        if (!deleted)
        {
            return Results.NotFound();
        }

        return Results.NoContent();
    }
}
