using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Models;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Magpie.Api.Services;

/// <summary>
/// Service for working with NuGet via the public v3 endpoints.
/// </summary>
/// <remarks>
/// Implements search via the search query service, package download via the
/// flat-container service, and version listing via the package metadata.
/// All endpoints fall back to the public nuget.org URLs when not configured.
/// </remarks>
public sealed class NuGetSearchService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<NuGetSearchService> logger) : INuGetService
{
    private const string DefaultSearchUrl = "https://azuresearch-usnc.nuget.org/query";
    private const string DefaultFlatContainerUrl = "https://api.nuget.org/v3-flatcontainer";
    private const string DefaultTags = "magpie-extension";

    /// <inheritdoc />
    public async Task<IEnumerable<NuGetSearchResult>> SearchAsync(string? query, CancellationToken cancellationToken)
    {
        var searchUrl = configuration["NuGet:SearchUrl"] ?? DefaultSearchUrl;
        var configuredTags = configuration["NuGet:Tags"] ?? DefaultTags;

        var tagList = configuredTags
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => $"tags:{t}");
        var tagQuery = string.Join(" ", tagList);

        var fullQuery = string.IsNullOrWhiteSpace(query)
            ? tagQuery
            : $"{query} {tagQuery}";

        var requestUrl = $"{searchUrl}?q={Uri.EscapeDataString(fullQuery)}&prerelease=false&semVerLevel=2.0.0";

        try
        {
            var response = await httpClient
                .GetFromJsonAsync<NuGetSearchResponse>(requestUrl, cancellationToken)
                .ConfigureAwait(false);

            if (response?.Data is null)
            {
                return [];
            }

            return response.Data
                .Select(p => new NuGetSearchResult
                {
                    Id = p.Id ?? string.Empty,
                    Version = p.Version ?? string.Empty,
                    Title = p.Title,
                    Description = p.Description,
                    Authors = p.Authors ?? [],
                    IconUrl = p.IconUrl,
                    ProjectUrl = p.ProjectUrl,
                    TotalDownloads = p.TotalDownloads,
                    Tags = p.Tags ?? []
                })
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error searching NuGet packages with query {Query} ({ExErrorCode})", query, "nuget-search");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<Stream?> DownloadPackageAsync(
        string packageId,
        string? version,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return null;
        }

        var resolvedVersion = version;
        if (string.IsNullOrWhiteSpace(resolvedVersion))
        {
            var versions = await GetVersionsAsync(packageId, cancellationToken).ConfigureAwait(false);
            resolvedVersion = versions.LastOrDefault();
            if (string.IsNullOrWhiteSpace(resolvedVersion))
            {
                return null;
            }
        }

        var flatContainer = configuration["NuGet:FlatContainerUrl"] ?? DefaultFlatContainerUrl;
        var lowerId = packageId.ToLowerInvariant();
        var lowerVersion = resolvedVersion!.ToLowerInvariant();
        var requestUrl = $"{flatContainer.TrimEnd('/')}/{lowerId}/{lowerVersion}/{lowerId}.{lowerVersion}.nupkg";

        try
        {
            var response = await httpClient
                .GetAsync(requestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            // Buffer the package into memory so callers can seek/zip-extract without keeping
            // the HTTP connection open.
            var buffer = new MemoryStream();
            await response.Content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            buffer.Position = 0;
            return buffer;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error downloading NuGet package {PackageId} {Version} ({ExErrorCode})",
                packageId,
                resolvedVersion,
                "nuget-download");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetVersionsAsync(string packageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return [];
        }

        var flatContainer = configuration["NuGet:FlatContainerUrl"] ?? DefaultFlatContainerUrl;
        var lowerId = packageId.ToLowerInvariant();
        var requestUrl = $"{flatContainer.TrimEnd('/')}/{lowerId}/index.json";

        try
        {
            var response = await httpClient
                .GetFromJsonAsync<NuGetVersionsResponse>(requestUrl, cancellationToken)
                .ConfigureAwait(false);
            return response?.Versions ?? [];
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error listing NuGet versions for {PackageId} ({ExErrorCode})",
                packageId,
                "nuget-versions");
            return [];
        }
    }

    private sealed record NuGetSearchResponse
    {
        [JsonPropertyName("totalHits")]
        public int TotalHits { get; init; }

        [JsonPropertyName("data")]
        public IReadOnlyList<NuGetPackage>? Data { get; init; }
    }

    private sealed record NuGetPackage
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("version")]
        public string? Version { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("authors")]
        public IReadOnlyList<string>? Authors { get; init; }

        [JsonPropertyName("iconUrl")]
        public string? IconUrl { get; init; }

        [JsonPropertyName("projectUrl")]
        public string? ProjectUrl { get; init; }

        [JsonPropertyName("totalDownloads")]
        public long TotalDownloads { get; init; }

        [JsonPropertyName("tags")]
        public IReadOnlyList<string>? Tags { get; init; }
    }

    private sealed record NuGetVersionsResponse
    {
        [JsonPropertyName("versions")]
        public IReadOnlyList<string>? Versions { get; init; }
    }
}
