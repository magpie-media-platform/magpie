using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="SubjectFilter"/>.
/// </summary>
/// <param name="Id">The <see cref="SubjectFilter.Id"/> of the item to patch.</param>
/// <param name="Name">The <see cref="SubjectFilter.Name"/> patch</param>
/// <param name="Criteria">The <see cref="SubjectFilter.Criteria"/> patch</param>
/// <param name="Tags">The <see cref="Tag"/>s patch</param>
public sealed record SubjectFilterPatch(
    Guid Id,
    ScalarPatch<string>? Name = null,
    ScalarPatch<SubjectFilterCriteria>? Criteria = null,
    SetPatch<string, string>? Tags = null) : TagsPatch(Id, Tags);
