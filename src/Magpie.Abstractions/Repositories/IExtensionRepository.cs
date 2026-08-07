using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for extensions.
/// </summary>
public interface IExtensionRepository :
    ICrudRepository<Guid, Extension>,
    IPatchRepository<Guid, Extension, ExtensionPatch>,
    IQueryRepository<Extension, ExtensionQuery>;
