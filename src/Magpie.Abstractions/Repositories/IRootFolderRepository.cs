using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for root folders.
/// </summary>
public interface IRootFolderRepository :
    ICrudRepository<Guid, RootFolder>,
    IPatchRepository<Guid, RootFolder, RootFolderPatch>;
