using System;
using System.Collections.Generic;

using Magpie.Models.Metadata;

namespace Magpie.Models.Content;

/// <summary>
/// A cross-cutting metadata entity that content relates to.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Kind">The kind of subject.</param>
/// <param name="DateLastRefreshed">The date the subject metadata was last refreshed.</param>
/// <param name="Metadata">The metadata container with layered provider contributions.</param>
/// <param name="Tags">Tags applied to this subject.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The user who created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The user who last modified the record.</param>
public sealed record Subject(
    Guid Id,
    string Kind,
    DateTimeOffset? DateLastRefreshed,
    MetadataContainer Metadata,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? ModifiedAt,
    Guid? ModifiedByUserId);
