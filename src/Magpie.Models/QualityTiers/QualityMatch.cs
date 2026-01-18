using System;

namespace Magpie.Models.QualityTiers;

/// <summary>
/// The result of matching a release against a quality tier list.
/// </summary>
/// <param name="TierListId">The tier list that was matched against.</param>
/// <param name="TierId">The matched tier, if any.</param>
/// <param name="TierOrdinal">The ordinal of the matched tier, if any.</param>
/// <param name="MeetsMinimum">Whether the release meets the minimum acceptable quality.</param>
/// <param name="MeetsCutoff">Whether the release meets or exceeds the cutoff tier.</param>
public sealed record QualityMatch(
    Guid TierListId,
    Guid? TierId,
    int? TierOrdinal,
    bool MeetsMinimum,
    bool MeetsCutoff);
