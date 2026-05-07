using System.Collections.Generic;

namespace Magpie.Models.Api;

/// <summary>
/// A NuGet package search result.
/// </summary>
/// <param name="Id">The NuGet package identifier.</param>
/// <param name="Version">The latest version of the package.</param>
/// <param name="Title">The package title.</param>
/// <param name="Description">The package description.</param>
/// <param name="Authors">The package authors.</param>
/// <param name="IconUrl">The package icon URL.</param>
/// <param name="ProjectUrl">The package project URL.</param>
/// <param name="TotalDownloads">The total download count.</param>
/// <param name="Tags">The package tags.</param>
public sealed record NuGetSearchResult(
    string Id,
    string Version,
    string? Title,
    string? Description,
    IReadOnlyList<string> Authors,
    string? IconUrl,
    string? ProjectUrl,
    long TotalDownloads,
    IReadOnlyList<string> Tags);
