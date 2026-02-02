using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="Tag"/>s. 
/// </summary>
/// <param name="Id">The id of the entity to patch.</param>
/// <param name="Tags">The <see cref="Tag"/>s to patch.</param>
public record TagsPatch(Guid Id, SetPatch<string, string>? Tags);
