using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

[MagpieCommandGroup("api-keys", "api-keys", "Manage API keys")]
internal sealed class ApiKeysEndpointGroup : IApiEndpointGroup
{
    public string Name => "api-keys";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetApiKeysAsync)
            .WithName(nameof(GetApiKeysAsync))
            .Produces<IReadOnlyList<ApiKeyResponse>>()
            .RequireAdmin();

        builder.MapPost("", CreateApiKeyAsync)
            .WithName(nameof(CreateApiKeyAsync))
            .Produces<ApiKeyCreatedResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireViewer();

        builder.MapGet("my", GetMyApiKeysAsync)
            .WithName(nameof(GetMyApiKeysAsync))
            .Produces<IReadOnlyList<ApiKeyResponse>>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetApiKeyAsync)
            .WithName(nameof(GetApiKeyAsync))
            .Produces<ApiKeyResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("{id:guid}", PatchApiKeyAsync)
            .WithName(nameof(PatchApiKeyAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapDelete("{id:guid}", DeleteApiKeyAsync)
            .WithName(nameof(DeleteApiKeyAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();
    }

    internal static IReadOnlyList<ApiKeyResponse> CreateResponse(IReadOnlyList<ApiKey> apiKeys) =>
        [.. apiKeys.Select(CreateResponse)];

    [MagpieCommand("list", "GET", "", "List all API keys")]
    internal async Task<IResult> GetApiKeysAsync(
        [FromServices] IApiKeyService apiKeyService,
        bool? enabled,
        bool? expired,
        CancellationToken cancellationToken)
    {
        var apiKeys = await apiKeyService.ListAllAsync(enabled, expired, cancellationToken).ConfigureAwait(false);

        return Results.Ok(CreateResponse(apiKeys));
    }

    [MagpieCommand("create", "POST", "", "Create an API key for the caller or a supplied username")]
    internal async Task<IResult> CreateApiKeyAsync(
        [FromServices] IApiKeyService apiKeyService,
        AuthenticatedUser user,
        [FromBody] ApiKeyCreate create,
        string? username,
        CancellationToken cancellationToken)
    {
        var created = string.IsNullOrWhiteSpace(username)
            ? await apiKeyService.CreateAsync(create, user.Id, cancellationToken).ConfigureAwait(false)
            : await apiKeyService
                .CreateForUserAsync(create, user.Id, username, cancellationToken)
                .ConfigureAwait(false);

        return Results.Created($"/api/{Name}/{created.ApiKey.Id}", CreateResponse(created));
    }

    internal async Task<IResult> GetMyApiKeysAsync(
        [FromServices] IApiKeyService apiKeyService,
        AuthenticatedUser user,
        bool? enabled,
        bool? expired,
        CancellationToken cancellationToken)
    {
        var apiKeys = await apiKeyService.ListOwnAsync(enabled, expired, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(CreateResponse(apiKeys));
    }

    [MagpieCommand("get", "GET", "{id:guid}", "Show an API key")]
    internal async Task<IResult> GetApiKeyAsync(
        [FromServices] IApiKeyService apiKeyService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var apiKey = await apiKeyService.GetAsync(id, user.Id, user.IsInRole(Role.Admin), cancellationToken)
            .ConfigureAwait(false);

        return apiKey is null ? Results.NotFound() : Results.Ok(CreateResponse(apiKey));
    }

    [MagpieCommand("patch", "PATCH", "{id:guid}", "Update fields on an API key")]
    internal async Task<IResult> PatchApiKeyAsync(
        [FromServices] IApiKeyService apiKeyService,
        AuthenticatedUser user,
        Guid id,
        [FromBody] ApiKeyPatch patch,
        CancellationToken cancellationToken)
    {
        var patched = await apiKeyService
            .PatchAsync(id, patch, user.Id, user.IsInRole(Role.Admin), cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    [MagpieCommand("delete", "DELETE", "{id:guid}", "Delete an API key")]
    internal async Task<IResult> DeleteApiKeyAsync(
        [FromServices] IApiKeyService apiKeyService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await apiKeyService.DeleteAsync(id, user.Id, user.IsInRole(Role.Admin), cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    private static ApiKeyCreatedResponse CreateResponse(ApiKeyWithPlaintextKey created) =>
        new() { Key = CreateResponse(created.ApiKey), PlaintextKey = created.PlaintextKey };

    private static ApiKeyResponse CreateResponse(ApiKey apiKey) =>
        new()
        {
            Id = apiKey.Id,
            UserId = apiKey.UserId,
            Name = apiKey.Name,
            KeyPrefix = apiKey.KeyPrefix,
            Enabled = apiKey.Enabled,
            ExpiresAt = apiKey.ExpiresAt,
            LastUsedAt = apiKey.LastUsedAt,
            CreatedAt = apiKey.CreatedAt,
            CreatedByUserId = apiKey.CreatedByUserId,
            ModifiedAt = apiKey.ModifiedAt,
            ModifiedByUserId = apiKey.ModifiedByUserId,
        };
}
