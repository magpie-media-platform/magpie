using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.UI;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;
using Magpie.Sdk.UI;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Magpie.Api.EndpointGroups;

internal sealed class UIEndpointGroup : IApiEndpointGroup
{
    public string Name => "ui";

    private static bool IsSafeRelativePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var normalized = relativePath.Replace('\\', '/');

        foreach (var segment in normalized.Split('/'))
        {
            if (segment == "..")
            {
                return false;
            }
        }

        if (normalized.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("manifest", GetManifest)
            .WithName(nameof(GetManifest))
            .Produces<UIManifest>()
            .RequireViewer();

        builder.MapGet("assets/{extensionId:guid}/{*relativePath}", GetAssetAsync)
            .WithName(nameof(GetAssetAsync))
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();
    }

    internal IResult GetManifest(
        [FromServices] IEnumerable<IUIExtensionProvider> uiExtensionProviders,
        [FromServices] IEnumerable<IAssetProvider> assetProviders,
        [FromServices] ILogger<UIEndpointGroup> logger)
    {
        var extensions = new List<UIExtension>();
        foreach (var provider in uiExtensionProviders)
        {
            try
            {
                extensions.AddRange(provider.GetUIExtensions());
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "UI extension provider {Provider} failed to list extensions",
                    provider.GetType().FullName);
            }
        }

        var assets = new List<Asset>();
        foreach (var provider in assetProviders)
        {
            try
            {
                assets.AddRange(provider.ListAssets());
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Asset provider {Provider} failed to list assets",
                    provider.GetType().FullName);
            }
        }

        return Results.Ok(new UIManifest { Extensions = extensions, Assets = assets });
    }

    internal async Task<IResult> GetAssetAsync(
        [FromServices] IEnumerable<IAssetProvider> assetProviders,
        [FromServices] IFileSystem fileSystem,
        [FromServices] ILogger<UIEndpointGroup> logger,
        Guid extensionId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Results.Problem(
                detail: "The relative path is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!IsSafeRelativePath(relativePath))
        {
            logger.LogWarning(
                "Asset request rejected for unsafe path {RelativePath} of extension {ExtensionId}",
                relativePath,
                extensionId);
            return Results.Problem(
                detail: "Path traversal is not allowed.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        IAssetProvider? owningProvider = null;
        foreach (var provider in assetProviders)
        {
            IEnumerable<Asset> assets;
            try
            {
                assets = provider.ListAssets();
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Asset provider {Provider} failed to list assets",
                    provider.GetType().FullName);
                continue;
            }

            if (assets.Any(asset => asset.ExtensionId == extensionId))
            {
                owningProvider = provider;
                break;
            }
        }

        if (owningProvider is null)
        {
            return Results.NotFound();
        }

        Stream? stream;
        try
        {
            stream = await owningProvider.GetAssetStreamAsync(relativePath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Asset provider {Provider} failed to resolve {RelativePath}",
                owningProvider.GetType().FullName,
                relativePath);
            return Results.NotFound();
        }

        if (stream is null)
        {
            return Results.NotFound();
        }

        var contentType = await fileSystem.LookupMimeTypeAsync(relativePath, cancellationToken)
            .ConfigureAwait(false);

        return Results.Stream(stream, contentType ?? "application/octet-stream");
    }
}
