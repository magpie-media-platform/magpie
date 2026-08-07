using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.UI;
using Magpie.Sdk.Api;
using Magpie.Sdk.UI;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Magpie.Api.EndpointGroups;

internal sealed class UIEndpointGroup(
    IEnumerable<IUIExtensionProvider> uiExtensionProviders,
    IEnumerable<IAssetProvider> assetProviders,
    ILogger<UIEndpointGroup> logger) : IApiEndpointGroup
{
    private readonly object _indexLock = new();
    private Dictionary<Guid, IAssetProvider>? _extensionIndex;

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

    private static string GuessContentType(string relativePath)
    {
        var ext = Path.GetExtension(relativePath).ToLowerInvariant();
        return ext switch
        {
            ".js" or ".mjs" => "application/javascript",
            ".css" => "text/css",
            ".json" => "application/json",
            ".html" or ".htm" => "text/html",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".map" => "application/json",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
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

    internal IResult GetManifest()
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
                    "The UI extension provider {Provider} threw an exception during the extension list. " +
                    "The endpoint skipped this provider",
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
                    "The asset provider {Provider} threw an exception during the asset list. " +
                    "The endpoint skipped this provider",
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

        var owningProvider = GetExtensionIndex().GetValueOrDefault(extensionId);

        var providersToProbe = owningProvider is not null
            ? [owningProvider]
            : assetProviders;

        foreach (var provider in providersToProbe)
        {
            Stream? stream;
            try
            {
                stream = await provider.GetAssetStreamAsync(relativePath, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "The asset provider {Provider} failed to resolve {RelativePath} and threw an exception. " +
                    "The endpoint skipped this provider",
                    provider.GetType().FullName,
                    relativePath);
                continue;
            }

            if (stream is null)
            {
                continue;
            }

            var contentType = GuessContentType(relativePath);
            return Results.Stream(stream, contentType);
        }

        return Results.NotFound();
    }

    private Dictionary<Guid, IAssetProvider> GetExtensionIndex()
    {
        if (_extensionIndex is { } cached)
        {
            return cached;
        }

        lock (_indexLock)
        {
            if (_extensionIndex is { } existing)
            {
                return existing;
            }

            var index = new Dictionary<Guid, IAssetProvider>();
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
                        "The asset provider {Provider} threw an exception during the asset list. " +
                        "The endpoint skipped this provider",
                        provider.GetType().FullName);
                    continue;
                }

                foreach (var asset in assets)
                {
                    index.TryAdd(asset.ExtensionId, provider);
                }
            }

            _extensionIndex = index;
            return index;
        }
    }
}
