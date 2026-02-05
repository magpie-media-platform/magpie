using System.Collections.Generic;

namespace Magpie.Models;

/// <summary>
/// A NuGet package search result.
/// </summary>
public sealed record NuGetSearchResult
{
    /// <summary>
    /// The NuGet package identifier.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// The latest version of the package.
    /// </summary>
    public required string Version { get; init; }
    /// <summary>
    /// The package title.
    /// </summary>
    public string? Title { get; init; }
    /// <summary>
    /// The package description.
    /// </summary>
    public string? Description { get; init; }
    /// <summary>
    /// The package authors.
    /// </summary>
    public IEnumerable<string> Authors { get; init; } = [];
    /// <summary>
    /// The package icon URL.
    /// </summary>
    public string? IconUrl { get; init; }
    /// <summary>
    /// The package project URL.
    /// </summary>
    public string? ProjectUrl { get; init; }
    /// <summary>
    /// The total download count.
    /// </summary>
    public long TotalDownloads { get; init; }
    /// <summary>
    /// The package tags.
    /// </summary>
    public IEnumerable<string> Tags { get; init; } = [];
}
