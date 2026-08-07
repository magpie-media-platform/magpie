using System;

using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for blocked file type rules.
/// </summary>
public interface IBlockedFileTypeRepository : ICrudRepository<Guid, BlockedFileType>;
