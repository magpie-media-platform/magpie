using System;

using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing root folder configurations.
/// </summary>
public interface IRootFolderRepository : ICrudRepository<Guid, RootFolder>
{
}
