using System;
using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Models.Arguments;

/// <summary>
/// Query arguments for <see cref="Release"/>.
/// </summary>d
public record ReleaseQuery
{
    /// <summary>
    /// The criteria for <see cref="Release.IndexerId"/>.
    /// </summary>
    public Guid? IndexerId { get; init; }

    /// <summary>
    /// The criteria for <see cref="Release.ContentItemId"/>.
    /// </summary>
    public Guid? ContentItemId { get; init; }

    /// <summary>
    /// The criteria for <see cref="Release.Title"/>.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// The criteria for <see cref="Release.Categories"/>.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// The criteria for <see cref="Release.ExternalId"/>.
    /// </summary>
    public string? ExternalId { get; init; }

    /// <summary>
    /// The criteria for <see cref="Release.PublishDate"/>.
    /// </summary>
    public Range<DateTime>? PublishDate { get; init; }

    /// <summary>
    /// The criteria for <see cref="Tags"/>s.
    /// </summary>
    public IEnumerable<string>? Tags { get; init; }
}
