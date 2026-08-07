using System.Collections.Generic;

namespace Magpie.Models.Api;

/// <summary>
/// A result of a search for NuGet packages.
/// </summary>
public sealed record NuGetSearchResult
{
    /// <summary>
    /// The id of the NuGet package.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The latest version of the package.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// The display title of the package.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// The description of the package.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The authors of the package.
    /// </summary>
    public IEnumerable<string> Authors { get; init; } = [];

    /// <summary>
    /// The URL of the icon image.
    /// </summary>
    public string? IconUrl { get; init; }

    /// <summary>
    /// The URL of the project.
    /// </summary>
    public string? ProjectUrl { get; init; }

    /// <summary>
    /// The total number of downloads.
    /// </summary>
    public long TotalDownloads { get; init; }

    /// <summary>
    /// The tags of the package.
    /// </summary>
    public IEnumerable<string> Tags { get; init; } = [];
}
