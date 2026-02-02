using System;

using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing indexers.
/// </summary>
public interface IIndexerRepository : ICrudRepository<Guid, Indexer>
{
}
