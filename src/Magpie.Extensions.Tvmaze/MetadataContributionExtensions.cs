using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Models.Data;

namespace Magpie.Extensions.Tvmaze;

internal static class MetadataContributionExtensions
{
    internal static string? FindExternalIdBySource(this IEnumerable<MetadataContribution> contributions, string source)
    {
        return contributions
            .SelectMany(contribution => contribution.ExternalIds
                .Where(externalId => string.Equals(externalId.Source, source, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(externalId.Value))
                .Select(externalId => new { contribution.ProviderType, externalId.Value }))
            .OrderByDescending(match => match.ProviderType == Constants.ProviderType)
            .Select(match => match.Value.Trim())
            .FirstOrDefault();
    }
}
