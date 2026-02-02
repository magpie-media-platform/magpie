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
        builder.MapGet("", GetApiKeys)
            .WithName("GetApiKeys")
            .WithTags("ApiKeys")
            .Produces<ApiKeyResponse[]>()
            .RequireAuthorization();

        builder.MapPost("", CreateApiKey)
            .WithName("CreateApiKey")
            .WithTags("ApiKeys")
            .Produces<ApiKeyCreatedResponse>(StatusCodes.Status201Created)
            .RequireAuthorization();

        builder.MapGet("/{id:guid}", GetApiKey)
            .WithName("GetApiKey")
            .WithTags("ApiKeys")
            .Produces<ApiKeyResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapPost("/{id:guid}/rotate", RotateApiKey)
            .WithName("RotateApiKey")
            .WithTags("ApiKeys")
            .Produces<ApiKeyCreatedResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapPost("/{id:guid}/enable", EnableApiKey)
            .WithName("EnableApiKey")
            .WithTags("ApiKeys")
            .Produces<ApiKeyResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapPost("/{id:guid}/disable", DisableApiKey)
            .WithName("DisableApiKey")
            .WithTags("ApiKeys")
            .Produces<ApiKeyResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapDelete("/{id:guid}", DeleteApiKey)
            .WithName("DeleteApiKey")
            .WithTags("ApiKeys")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private async Task<IResult> GetApiKeys(
        Guid? userId,
        bool? enabled,
        bool? expired,
        CancellationToken cancellationToken)
    {
        var apiKeys = await apiKeyManager.ListApiKeysAsync(userId, enabled, expired, cancellationToken)
            .ConfigureAwait(false);

        var responses = apiKeys.Select(k => k.ToResponse()).ToArray();
        return Results.Ok(responses);
    }

    private async Task<IResult> CreateApiKey(
        HttpContext httpContext,
        CreateApiKeyRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var result = await apiKeyManager.CreateApiKeyAsync(
            request.UserId,
            request.Name,
            request.ExpiresAt,
            currentUserId,
            cancellationToken).ConfigureAwait(false);

        return Results.Created($"/{Name}/{result.ApiKey.Id}", result.ToCreatedResponse());
    }

    private async Task<IResult> GetApiKey(Guid id, CancellationToken cancellationToken)
    {
        var apiKey = await apiKeyManager.GetApiKeyAsync(id, cancellationToken).ConfigureAwait(false);

        if (apiKey is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(apiKey.ToResponse());
    }

    private async Task<IResult> RotateApiKey(HttpContext httpContext, Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var existing = await apiKeyManager.GetApiKeyAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return Results.NotFound();
        }

        var result = await apiKeyManager.RotateApiKeyAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result.ToCreatedResponse());
    }

    private async Task<IResult> EnableApiKey(HttpContext httpContext, Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var existing = await apiKeyManager.GetApiKeyAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return Results.NotFound();
        }

        var apiKey = await apiKeyManager.EnableApiKeyAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(apiKey.ToResponse());
    }

    private async Task<IResult> DisableApiKey(HttpContext httpContext, Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();

        var existing = await apiKeyManager.GetApiKeyAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return Results.NotFound();
        }

        var apiKey = await apiKeyManager.DisableApiKeyAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(apiKey.ToResponse());
    }

    private async Task<IResult> DeleteApiKey(HttpContext httpContext, Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var deleted = await apiKeyManager.DeleteApiKeyAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        if (!deleted)
        {
            return Results.NotFound();
        }

        return Results.NoContent();
    }
}
