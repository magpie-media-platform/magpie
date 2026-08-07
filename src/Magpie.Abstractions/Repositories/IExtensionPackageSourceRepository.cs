using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for extension NuGet package sources.
/// </summary>
public interface IExtensionPackageSourceRepository :
    ICrudRepository<Guid, ExtensionPackageSource>,
    IPatchRepository<Guid, ExtensionPackageSource, ExtensionPackageSourcePatch>,
    IQueryRepository<ExtensionPackageSource, ExtensionPackageSourceQuery>;
