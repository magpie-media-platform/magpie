using System;
using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Models.Arguments;

/// <summary>
/// Query arguments for <see cref="Release"/>.
/// </summary>
/// <param name="IndexerId">The criteria for <see cref="Release.IndexerId"/>.</param>
/// <param name="ExternalId">The criteria for <see cref="Release.ExternalId"/>.</param>
/// <param name="ContentItemId">The criteria for <see cref="Release.ContentItemId"/>.</param>
/// <param name="Title">The criteria for <see cref="Release.Title"/>.</param>
/// <param name="Category">The criteria for <see cref="Release.Categories"/>.</param>
/// <param name="PublishDate">The criteria for <see cref="Release.PublishDate"/>.</param>
/// <param name="Tags">The criteria for <see cref="Tags"/>s.</param>
public record ReleaseQuery(
    Guid? IndexerId = null,
    Guid? ContentItemId = null,
    string? Title = null,
    string? Category = null,
    string? ExternalId = null,
    Range<DateTime>? PublishDate = null,
    IEnumerable<string>? Tags = null);
