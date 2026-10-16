using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The read contract for indexer connections.
/// </summary>
public interface IIndexerConnectionRepository :
    IQueryRepository<IndexerConnection, IndexerConnectionQuery>,
    IKeyedRepository<Guid, IndexerConnection>
{
}
