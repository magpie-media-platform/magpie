using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for indexers.
/// </summary>
public interface IIndexerRepository :
    IQueryRepository<Indexer, IndexerQuery>,
    ICrudRepository<Guid, Indexer>,
    IPatchRepository<Guid, Indexer, IndexerPatch>;
