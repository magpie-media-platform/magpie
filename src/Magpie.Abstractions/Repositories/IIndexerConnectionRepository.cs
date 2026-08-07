using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for indexer connections.
/// </summary>
public interface IIndexerConnectionRepository :
    IQueryRepository<IndexerConnection, IndexerConnectionQuery>,
    ICrudRepository<Guid, IndexerConnection>,
    IPatchRepository<Guid, IndexerConnection, IndexerConnectionPatch>
{
}
