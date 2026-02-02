using System.Collections.Generic;

namespace Magpie.Models.Patch;

/// <summary>
/// A patch for arrays of values with indexed update operations.
/// </summary>
/// <typeparam name="TPatch">The type of patch operation for existing items.</typeparam>
/// <param name="Update"><typeparamref name="TPatch"/> patches at specific indexes in the array.</param>
public record ArrayPatch<TPatch>(IReadOnlyDictionary<int, TPatch>? Update = null);
