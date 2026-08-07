using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for content filters.
/// </summary>
public interface IContentFilterRepository :
    IQueryRepository<ContentFilter, ContentFilterQuery>,
    ICrudRepository<Guid, ContentFilter>,
    IPatchRepository<Guid, ContentFilter, ContentFilterPatch>;
