using System;

using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for root folders.
/// </summary>
public interface IRootFolderRepository : ICrudRepository<Guid, RootFolder>;
