using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The fields to change on the row of a <see cref="Subject"/>.
/// </summary>
public sealed record SubjectRowPatch : TagsPatch
{
    /// <summary>
    /// The patch for <see cref="Subject.DateLastRefreshed"/>.
    /// </summary>
    public ScalarPatch<DateTime?>? DateLastRefreshed { get; init; }
}
