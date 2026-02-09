using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="Award"/>.
/// </summary>
public sealed record AwardPatch
{
    /// <summary>
    /// The <see cref="Award.Id"/> of the item to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The <see cref="Award.Ordinal"/> to patch.
    /// </summary>
    public ScalarPatch<int>? Ordinal { get; init; }

    /// <summary>
    /// The <see cref="Award.Body"/> to patch.
    /// </summary>
    public ScalarPatch<string>? Body { get; init; }

    /// <summary>
    /// The <see cref="Award.Name"/> to patch.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The <see cref="Award.Category"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? Category { get; init; }

    /// <summary>
    /// The <see cref="Award.Status"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? Status { get; init; }

    /// <summary>
    /// The <see cref="Award.Year"/> to patch.
    /// </summary>
    public ScalarPatch<int?>? Year { get; init; }

    /// <summary>
    /// The <see cref="Award.Language"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? Language { get; init; }

    /// <summary>
    /// The <see cref="Award.Country"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? Country { get; init; }
}
