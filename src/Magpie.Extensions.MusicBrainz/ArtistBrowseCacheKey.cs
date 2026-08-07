using System;
using System.Collections.Generic;
using System.Globalization;

using Magpie.Extensions.MusicBrainz.QueryEngine;

namespace Magpie.Extensions.MusicBrainz;

internal sealed record ArtistBrowseCacheKey
{
    public required ArtistCompositionEntity RelatedEntity { get; init; }

    public required string RelatedMbid { get; init; }

    public LookupCapability Capabilities { get; init; }

    public required int Limit { get; init; }

    public required int Offset { get; init; }

    public string? RequestTarget
    {
        get
        {
            var parameter = RelatedEntity switch
            {
                ArtistCompositionEntity.ReleaseGroup => "release-group",
                ArtistCompositionEntity.Release => "release",
                ArtistCompositionEntity.Recording => "recording",
                ArtistCompositionEntity.Work => "work",
                _ => null
            };
            if (parameter is null)
            {
                return null;
            }

            var includes = new List<string>();
            AddInclude(includes, LookupCapability.Aliases, "aliases");
            AddInclude(includes, LookupCapability.Genres, "genres");
            AddInclude(includes, LookupCapability.UrlRelations, "url-rels");
            var includeParameter = includes.Count == 0 ? string.Empty : "&inc=" + string.Join("+", includes);
            return string.Format(
                CultureInfo.InvariantCulture,
                "artist?fmt=json&{0}={1}{2}&limit={3}&offset={4}",
                parameter,
                Uri.EscapeDataString(RelatedMbid),
                includeParameter,
                Limit,
                Offset);
        }
    }

    private void AddInclude(List<string> includes, LookupCapability capability, string include)
    {
        if ((Capabilities & capability) != 0)
        {
            includes.Add(include);
        }
    }
}
