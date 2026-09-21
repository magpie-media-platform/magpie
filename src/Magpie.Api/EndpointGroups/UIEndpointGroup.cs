using System;
using System.Collections.Generic;
using System.IO;
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
    private Dictionary<Guid, IAssetProvider>? _assetOwners;

    public string Name => "ui";

    private static bool IsSafeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

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

    private static Dictionary<Guid, IAssetProvider> BuildAssetOwners(
        IEnumerable<IAssetProvider> assetProviders,
        ILogger logger)
    {
        var owners = new Dictionary<Guid, IAssetProvider>();
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
                    "The asset provider {Provider} failed to list assets, so the endpoint skipped it",
                    provider.GetType().FullName);
                continue;
            }

            foreach (var asset in assets)
            {
                owners.TryAdd(asset.ExtensionId, provider);
            }
        }

        return owners;
    }

    private IAssetProvider? ResolveOwningProvider(
        IEnumerable<IAssetProvider> assetProviders,
        Guid extensionId,
        ILogger logger)
    {
        return (_assetOwners ??= BuildAssetOwners(assetProviders, logger)).GetValueOrDefault(extensionId);
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
                    "The UI extension provider {Provider} failed to list extensions, so the endpoint skipped it",
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
                    "The asset provider {Provider} failed to list assets, so the endpoint skipped it",
                    provider.GetType().FullName);
            }
        }

        return Results.Ok(new UIManifest
        {
            Extensions = extensions,
            Assets = assets
        });
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
                "The endpoint rejected the asset request with unsafe path {RelativePath} for extension {ExtensionId}",
                relativePath,
                extensionId);
            return Results.Problem(
                detail: "Path traversal is not allowed.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var owningProvider = ResolveOwningProvider(assetProviders, extensionId, logger);
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
                "The asset provider {Provider} failed to resolve {RelativePath}",
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
