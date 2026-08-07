using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Magpie.Models.Data;

namespace Magpie.Extensions.Tmdb;

internal static class MetadataContributionExtensions
{
    internal static string? FindTmdbId(this IEnumerable<MetadataContribution> contributions)
    {
        return contributions
            .SelectMany(contribution => (contribution.ExternalIds ?? [])
                .Where(externalId => externalId.Source == Constants.ProviderType &&
                    !string.IsNullOrWhiteSpace(externalId.Value))
                .Select(externalId => new { contribution.ProviderType, externalId.Value }))
            .OrderByDescending(match => match.ProviderType == Constants.ProviderType)
            .Select(match => match.Value)
            .FirstOrDefault();
    }

    internal static string? FindSequenceNumber(this IEnumerable<MetadataContribution> contributions)
    {
        return contributions
            .Select(contribution => contribution.SequenceNumber)
            .FirstOrDefault(sequence => sequence is not null)?.ToString(CultureInfo.InvariantCulture);
    }
}
