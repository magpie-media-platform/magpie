using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.UI;
using Magpie.Models.UI;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Magpie.Api.EndpointGroups;

/// <summary>
/// Endpoints that expose extension-provided UI metadata and static assets to the SPA.
/// </summary>
/// <remarks>
/// <para>
/// The SPA fetches <c>GET /api/ui/manifest</c> at boot to discover slot contributions and
/// asset URLs, then loads each asset via <c>GET /api/ui/assets/{extensionId}/{relativePath}</c>.
/// </para>
/// <para>
/// Asset serving delegates to the extension that registered itself as an
/// <see cref="IAssetProvider"/>. The provider maps a <c>relativePath</c> to a stream;
/// this endpoint validates the path (rejecting path traversal) and forwards the stream
/// with a content-type guess derived from the extension.
/// </para>
/// <para>
/// To avoid an N x M scan on every asset request, the endpoint maintains a lazily-built
/// extensionId -> provider index. Because extensions are loaded once at host startup
/// and providers are not added/removed at runtime, the index is built on first asset
/// request and reused for the lifetime of the host.
/// </para>
/// </remarks>
internal sealed class UIEndpointGroup(
    IEnumerable<IUIExtensionProvider> uiExtensionProviders,
    IEnumerable<IAssetProvider> assetProviders,
    ILogger<UIEndpointGroup> logger) : IApiEndpointGroup
{
    private readonly object _indexLock = new();
    private Dictionary<Guid, IAssetProvider>? _extensionIndex;

    public string Name => "ui";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("manifest", GetManifest)
            .WithName(nameof(GetManifest))
            .Produces<UIManifest>()
            .RequireViewer();

        builder.MapGet("assets/{extensionId:guid}/{*relativePath}", GetAsset)
            .WithName(nameof(GetAsset))
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();
    }

    private IResult GetManifest()
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
                    "UI extension provider '{Provider}' threw while listing extensions; skipping.",
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
                    "Asset provider '{Provider}' threw while listing assets; skipping.",
                    provider.GetType().FullName);
            }
        }

        return Results.Ok(new UIManifest
        {
            Extensions = extensions,
            Assets = assets
        });
    }

    private async Task<IResult> GetAsset(
        Guid extensionId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Results.BadRequest(new { error = "relativePath is required." });
        }

        if (!IsSafeRelativePath(relativePath))
        {
            logger.LogWarning(
                "Rejected asset request with unsafe relative path '{RelativePath}' for extension {ExtensionId}.",
                relativePath,
                extensionId);
            return Results.BadRequest(new { error = "Path traversal is not allowed." });
        }

        // Look up the owning provider via a cached extensionId -> provider index that is
        // built once at first call. Providers may serve nested files they do not advertise
        // in ListAssets, so when no provider owns the extension we fall back to probing
        // every provider.
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
                    "Asset provider '{Provider}' threw while resolving '{RelativePath}'.",
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

    /// <summary>
    /// Returns the cached extensionId -> provider map, building it on first access.
    /// </summary>
    /// <remarks>
    /// Asset providers are loaded once at host startup; if a provider's asset list
    /// changes after the first request, those changes are not reflected here.
    /// </remarks>
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
                        "Asset provider '{Provider}' threw while listing assets; skipping.",
                        provider.GetType().FullName);
                    continue;
                }

                foreach (var asset in assets)
                {
                    // First provider wins; if multiple advertise the same extension we keep
                    // the first one and rely on the per-request fallback to probe others.
                    index.TryAdd(asset.ExtensionId, provider);
                }
            }

            _extensionIndex = index;
            return index;
        }
    }

    /// <summary>
    /// Rejects relative paths that contain traversal segments, absolute roots, or other
    /// patterns that could escape the extension directory.
    /// </summary>
    internal static bool IsSafeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        if (Path.IsPathRooted(relativePath))
        {
            return false;
        }

        // Disallow any segment equal to "..", any drive prefix, and any backslash escape.
        var normalized = relativePath.Replace('\\', '/');

        foreach (var segment in normalized.Split('/'))
        {
            if (segment == "..")
            {
                return false;
            }
        }

        // Reject Windows drive prefixes that might slip through (e.g. "C:foo").
        if (normalized.Contains(':'))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Maps a file extension to a reasonable content type.
    /// </summary>
    internal static string GuessContentType(string relativePath)
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
}
