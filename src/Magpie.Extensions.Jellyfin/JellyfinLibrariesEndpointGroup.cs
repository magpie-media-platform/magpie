using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Jellyfin.Models;
using Magpie.Models;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Jellyfin;

internal sealed class JellyfinLibrariesEndpointGroup : IApiEndpointGroup
{
    public string Name => "jellyfin";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapPost("libraries", ListLibrariesAsync)
            .WithName(nameof(ListLibrariesAsync))
            .Produces<IReadOnlyList<SettingSchemaOption>>()
            .RequireManager();
    }

    internal async Task<IResult> ListLibrariesAsync(
        [FromServices] IMediaServerService mediaServerService,
        [FromServices] IJellyfinClient client,
        [FromServices] ILogger<JellyfinLibrariesEndpointGroup> logger,
        [FromBody] JellyfinLibrariesRequest request,
        CancellationToken cancellationToken)
    {
        var merged = await mediaServerService
            .ResolveSettingsAsync(
                request.MediaServerId,
                request.Settings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                cancellationToken)
            .ConfigureAwait(false);

        if (!merged.TryGetValueIgnoringCase(JellyfinSettings.HostKey, out var rawHost) ||
            string.IsNullOrWhiteSpace(rawHost) ||
            !merged.TryGetValueIgnoringCase(JellyfinSettings.ApiKeyKey, out var apiKey) ||
            string.IsNullOrEmpty(apiKey))
        {
            return Results.Ok(Array.Empty<SettingSchemaOption>());
        }

        JellyfinSettings parsed;
        try
        {
            parsed = merged.ToJellyfinSettings();
        }
        catch (ArgumentException ex)
        {
            logger.LogInformation(ex, "The Jellyfin library listing received invalid settings for {Host}.", rawHost);
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway);
        }

        IReadOnlyList<VirtualFolder> folders;
        try
        {
            folders = await client.GetVirtualFoldersAsync(parsed, cancellationToken).ConfigureAwait(false);
        }
        catch (JellyfinAuthenticationException ex)
        {
            logger.LogInformation(ex, "Jellyfin at {Host} refused the API key for the library listing.", parsed.Host);
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "The Jellyfin library listing failed for {Host}.", parsed.Host);
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway);
        }

        var options = new List<SettingSchemaOption>(folders.Count);
        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder.ItemId))
            {
                continue;
            }

            options.Add(new SettingSchemaOption
            {
                Value = folder.ItemId!,
                Label = string.IsNullOrWhiteSpace(folder.Name) ? folder.ItemId! : folder.Name!,
                Description = folder.CollectionType
            });
        }

        return Results.Ok(options);
    }
}
