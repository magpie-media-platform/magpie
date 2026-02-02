using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="ContentFilter"/>.
/// </summary>
/// <param name="Id">The <see cref="ContentFilter.Id"/> of the item to patch.</param>
/// <param name="Name">The <see cref="ContentFilter.Name"/> patch</param>
/// <param name="Criteria">The <see cref="ContentFilter.Criteria"/> patch</param>
/// <param name="OwnerLock">The <see cref="ContentFilter.OwnerLock"/> patch</param>
/// <param name="Tags">The <see cref="Tag"/>s patch</param>
public sealed record ContentFilterPatch(
    Guid Id,
    ScalarPatch<string>? Name = null,
    ScalarPatch<ContentFilterCriteria>? Criteria = null,
    ScalarPatch<bool?>? OwnerLock = null,
    SetPatch<string, string>? Tags = null) : TagsPatch(Id, Tags);
