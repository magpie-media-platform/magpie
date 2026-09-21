using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for indexer feeds.
/// </summary>
public interface IIndexerFeedRepository :
    ICrudRepository<Guid, IndexerFeed>,
    IPatchRepository<Guid, IndexerFeed, IndexerFeedPatch>
{
}
