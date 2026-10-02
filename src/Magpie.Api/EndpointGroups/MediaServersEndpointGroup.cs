using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

[MagpieCommandGroup("media-servers", "media-servers", "Manage media servers")]
internal sealed class MediaServersEndpointGroup : IApiEndpointGroup
{
    public string Name => "media-servers";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetMediaServersAsync)
            .WithName(nameof(GetMediaServersAsync))
            .Produces<IReadOnlyList<MediaServer>>()
            .RequireViewer();

        builder.MapPost("", CreateMediaServerAsync)
            .WithName(nameof(CreateMediaServerAsync))
            .Produces<MediaServer>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAdmin();

        builder.MapPost("test", TestMediaServerSettingsAsync)
            .WithName(nameof(TestMediaServerSettingsAsync))
            .Produces<ConfigurationTestResult>()
            .RequireAdmin();

        builder.MapGet("{id:guid}", GetMediaServerAsync)
            .WithName(nameof(GetMediaServerAsync))
            .Produces<MediaServer>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("{id:guid}", PatchMediaServerAsync)
            .WithName(nameof(PatchMediaServerAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapDelete("{id:guid}", DeleteMediaServerAsync)
            .WithName(nameof(DeleteMediaServerAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapPost("{id:guid}/test", TestMediaServerAsync)
            .WithName(nameof(TestMediaServerAsync))
            .Produces<ConfigurationTestResult>()
            .RequireAdmin();
    }

    [MagpieCommand("list", "GET", "", "List media servers")]
    internal async Task<IResult> GetMediaServersAsync(
        [FromServices] IMediaServerService mediaServerService,
        CancellationToken cancellationToken)
    {
        var servers = await mediaServerService.ListAsync(cancellationToken).ConfigureAwait(false);

        var redacted = new List<MediaServer>(servers.Count);
        foreach (var server in servers)
        {
            redacted.Add(await RedactSecretsAsync(mediaServerService, server, cancellationToken).ConfigureAwait(false));
        }

        return Results.Ok(redacted);
    }

    [MagpieCommand("create", "POST", "", "Create a media server")]
    internal async Task<IResult> CreateMediaServerAsync(
        [FromServices] IMediaServerService mediaServerService,
        AuthenticatedUser user,
        [FromBody] MediaServer server,
        CancellationToken cancellationToken)
    {
        var created = await mediaServerService.CreateAsync(server, user.Id, cancellationToken).ConfigureAwait(false);

        var response = await RedactSecretsAsync(mediaServerService, created, cancellationToken).ConfigureAwait(false);

        return Results.Created($"/api/{Name}/{created.Id}", response);
    }

    [MagpieCommand("test-settings", "POST", "test", "Test media server settings")]
    internal async Task<IResult> TestMediaServerSettingsAsync(
        [FromServices] IMediaServerService mediaServerService,
        [FromBody] IntegrationSettingsTestRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediaServerService.TestSettingsAsync(
            request.ProviderType,
            request.Id,
            request.Settings ?? new Dictionary<string, string>(),
            cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    [MagpieCommand("get", "GET", "{id:guid}", "Show a media server")]
    internal async Task<IResult> GetMediaServerAsync(
        [FromServices] IMediaServerService mediaServerService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var server = await mediaServerService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (server is null)
        {
            return Results.NotFound();
        }

        var response = await RedactSecretsAsync(mediaServerService, server, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(response);
    }

    [MagpieCommand("patch", "PATCH", "{id:guid}", "Update fields on a media server")]
    internal async Task<IResult> PatchMediaServerAsync(
        [FromServices] IMediaServerService mediaServerService,
        AuthenticatedUser user,
        Guid id,
        [FromBody] MediaServerPatch patch,
        CancellationToken cancellationToken)
    {
        var patched = await mediaServerService.PatchAsync(id, patch, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    [MagpieCommand("delete", "DELETE", "{id:guid}", "Delete a media server")]
    internal async Task<IResult> DeleteMediaServerAsync(
        [FromServices] IMediaServerService mediaServerService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await mediaServerService.DeleteAsync(id, user.Id, cancellationToken).ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    [MagpieCommand("test", "POST", "{id:guid}/test", "Test a media server")]
    internal async Task<IResult> TestMediaServerAsync(
        [FromServices] IMediaServerService mediaServerService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await mediaServerService.TestAsync(id, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<MediaServer> RedactSecretsAsync(
        IMediaServerService mediaServerService,
        MediaServer server,
        CancellationToken cancellationToken)
    {
        var schema = await mediaServerService.GetSettingsSchemaAsync(server.ProviderType, cancellationToken)
            .ConfigureAwait(false);

        return server with { Settings = server.Settings.RedactSecrets(schema) };
    }
}
