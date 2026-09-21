using System.Collections.Generic;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a <see cref="LibraryImportCandidate"/>.
/// </summary>
public sealed record LibraryImportCandidatePatch
{
    /// <summary>
    /// The patch for the metadata matches.
    /// </summary>
    public ScalarPatch<List<LibraryImportMatch>>? Matches { get; init; }
}
