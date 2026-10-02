using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
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

[MagpieCommandGroup("download-clients", "download-clients", "Manage download clients")]
internal sealed class DownloadClientsEndpointGroup : IApiEndpointGroup
{
    public string Name => "download-clients";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetDownloadClientsAsync)
            .WithName(nameof(GetDownloadClientsAsync))
            .Produces<IReadOnlyList<DownloadClient>>()
            .RequireViewer();

        builder.MapPost("", CreateDownloadClientAsync)
            .WithName(nameof(CreateDownloadClientAsync))
            .Produces<DownloadClient>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAdmin();

        builder.MapPost("test", TestDownloadClientSettingsAsync)
            .WithName(nameof(TestDownloadClientSettingsAsync))
            .Produces<ConfigurationTestResult>()
            .RequireAdmin();

        builder.MapGet("{id:guid}", GetDownloadClientAsync)
            .WithName(nameof(GetDownloadClientAsync))
            .Produces<DownloadClient>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("{id:guid}", PatchDownloadClientAsync)
            .WithName(nameof(PatchDownloadClientAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapDelete("{id:guid}", DeleteDownloadClientAsync)
            .WithName(nameof(DeleteDownloadClientAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapPost("{id:guid}/test", TestDownloadClientAsync)
            .WithName(nameof(TestDownloadClientAsync))
            .Produces<ConfigurationTestResult>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();
    }

    [MagpieCommand("list", "GET", "", "List download clients")]
    internal async Task<IResult> GetDownloadClientsAsync(
        [FromServices] IDownloadClientService downloadClientService,
        CancellationToken cancellationToken)
    {
        var clients = await downloadClientService.ListAsync(cancellationToken).ConfigureAwait(false);

        var redacted = new List<DownloadClient>(clients.Count);
        foreach (var client in clients)
        {
            var schema = await downloadClientService
                .GetSettingsSchemaAsync(client.ProviderType, cancellationToken)
                .ConfigureAwait(false);
            redacted.Add(RedactSecrets(client, schema));
        }

        return Results.Ok<IReadOnlyList<DownloadClient>>(redacted);
    }

    [MagpieCommand("create", "POST", "", "Create a download client")]
    internal async Task<IResult> CreateDownloadClientAsync(
        [FromServices] IDownloadClientService downloadClientService,
        AuthenticatedUser user,
        [FromBody] DownloadClient client,
        CancellationToken cancellationToken)
    {
        var created = await downloadClientService.CreateAsync(client, user.Id, cancellationToken)
            .ConfigureAwait(false);
        var schema = await downloadClientService.GetSettingsSchemaAsync(created.ProviderType, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"/api/{Name}/{created.Id}", RedactSecrets(created, schema));
    }

    [MagpieCommand("test-settings", "POST", "test", "Test download client settings")]
    internal async Task<IResult> TestDownloadClientSettingsAsync(
        [FromServices] IDownloadClientService downloadClientService,
        [FromBody] IntegrationSettingsTestRequest request,
        CancellationToken cancellationToken)
    {
        var result = await downloadClientService.TestSettingsAsync(
            request.ProviderType,
            request.Id,
            request.Settings ?? new Dictionary<string, string>(),
            cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    [MagpieCommand("get", "GET", "{id:guid}", "Show a download client")]
    internal async Task<IResult> GetDownloadClientAsync(
        [FromServices] IDownloadClientService downloadClientService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var client = await downloadClientService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (client is null)
        {
            return Results.NotFound();
        }

        var schema = await downloadClientService.GetSettingsSchemaAsync(client.ProviderType, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(RedactSecrets(client, schema));
    }

    [MagpieCommand("patch", "PATCH", "{id:guid}", "Update fields on a download client")]
    internal async Task<IResult> PatchDownloadClientAsync(
        [FromServices] IDownloadClientService downloadClientService,
        AuthenticatedUser user,
        Guid id,
        [FromBody] DownloadClientPatch patch,
        CancellationToken cancellationToken)
    {
        var patched = await downloadClientService.PatchAsync(id, patch, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    [MagpieCommand("delete", "DELETE", "{id:guid}", "Delete a download client")]
    internal async Task<IResult> DeleteDownloadClientAsync(
        [FromServices] IDownloadClientService downloadClientService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await downloadClientService.DeleteAsync(id, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    [MagpieCommand("test", "POST", "{id:guid}/test", "Test a download client")]
    internal async Task<IResult> TestDownloadClientAsync(
        [FromServices] IDownloadClientService downloadClientService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await downloadClientService.TestAsync(id, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static DownloadClient RedactSecrets(DownloadClient client, IReadOnlyList<SettingSchemaDefinition> schema)
    {
        return client with { Settings = client.Settings.RedactSecrets(schema) };
    }
}
