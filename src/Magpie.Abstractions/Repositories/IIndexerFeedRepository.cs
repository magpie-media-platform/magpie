using System;

using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing indexer feeds.
/// </summary>
public interface IIndexerFeedRepository : ICrudRepository<Guid, IndexerFeed>
{
}
