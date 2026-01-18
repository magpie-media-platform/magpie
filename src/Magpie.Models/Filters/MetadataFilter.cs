using System;
using System.Collections.Generic;

using Magpie.Models.Filters.Metadata;

namespace Magpie.Models.Filters;

/// <summary>
/// A saved filter for metadata (subjects, etc.).
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="Criteria">The filter criteria.</param>
/// <param name="Tags">Tags applied to this filter.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The user who created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The user who last modified the record.</param>
public sealed record MetadataFilter(
    Guid Id,
    string Name,
    MetadataContributionCriteria Criteria,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? ModifiedAt,
    Guid? ModifiedByUserId);
