using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for content items and content aggregate rows.
/// </summary>
public interface IContentRepository :
    ICrudRepository<Guid, ContentItem>,
    IQueryRepository<ContentItem, ContentItemQuery>;
