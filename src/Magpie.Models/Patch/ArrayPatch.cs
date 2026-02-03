using System.Collections.Generic;

namespace Magpie.Models.Patch;

/// <summary>
/// A patch for arrays of values with indexed update operations.
/// </summary>
/// <typeparam name="TPatch">The type of patch operation for existing items.</typeparam>
public record ArrayPatch<TPatch>
{
    /// <summary>
    /// <typeparamref name="TPatch"/> patches at specific indexes in the array. 
    /// </summary>
    public Dictionary<int, TPatch>? Update { get; init; }
}
