using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="Award"/>.
/// </summary>
/// <param name="Id">The <see cref="Award.Id"/> of the item to patch.</param>
/// <param name="Ordinal">The <see cref="Award.Ordinal"/> patch</param>
/// <param name="AwardBody">The <see cref="Award.AwardBody"/> patch</param>
/// <param name="Name">The <see cref="Award.Name"/> patch</param>
/// <param name="Category">The <see cref="Award.Category"/> patch</param>
/// <param name="Status">The <see cref="Award.Status"/> patch</param>
/// <param name="Year">The <see cref="Award.Year"/> patch</param>
/// <param name="Language">The <see cref="Award.Language"/> patch</param>
/// <param name="Country">The <see cref="Award.Country"/> patch</param>
public sealed record AwardPatch(
    Guid Id,
    ScalarPatch<int>? Ordinal = null,
    ScalarPatch<string>? AwardBody = null,
    ScalarPatch<string>? Name = null,
    ScalarPatch<string?>? Category = null,
    ScalarPatch<string?>? Status = null,
    ScalarPatch<int?>? Year = null,
    ScalarPatch<string?>? Language = null,
    ScalarPatch<string?>? Country = null);
