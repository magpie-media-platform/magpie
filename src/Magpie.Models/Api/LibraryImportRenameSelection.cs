using System;
using System.Collections.Generic;

using Magpie.Models.Arguments;

namespace Magpie.Models.Api;

/// <summary>
/// Selects import candidates for a read-only rename preview.
/// </summary>
public sealed record LibraryImportRenameSelection
{
    /// <summary>
    /// The explicitly selected candidates.
    /// </summary>
    public IReadOnlyList<Guid> Ids { get; init; } = [];

    /// <summary>
    /// The candidate or group filters to include.
    /// </summary>
    public IReadOnlyList<LibraryImportCandidateQuery> Filters { get; init; } = [];
}
