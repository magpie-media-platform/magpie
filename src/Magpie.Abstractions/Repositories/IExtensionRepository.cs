using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing extensions.
/// </summary>
public interface IExtensionRepository :
    ICrudRepository<Guid, Extension>,
    IPatchRepository<Guid, Extension, ExtensionPatch>
{
}
