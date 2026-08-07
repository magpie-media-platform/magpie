using System;
using System.Collections.Generic;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal static class ReferenceProjectionExtensions
{
    private const string DiscogsKind = "discogs";
    private const string WikidataKind = "wikidata";

    public static IReadOnlyList<ProjectedReference> Project(this IReadOnlyList<RelationDto> relations)
    {
        var result = new List<ProjectedReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relation in relations)
        {
            var type = relation.Type?.Trim();
            var resource = relation.Url?.Resource?.Trim();
            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(resource) ||
                !Uri.TryCreate(resource, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https"))
            {
                continue;
            }

            var kind = NormalizeKind(type, uri.Host);
            if (kind is null || !seen.Add(kind + "\0" + uri.AbsoluteUri))
            {
                continue;
            }

            result.Add(new ProjectedReference { Kind = kind, Url = uri });
        }

        return result;
    }

    private static string? NormalizeKind(string type, string host)
    {
        if (MatchesHost(host, "discogs.com"))
        {
            return DiscogsKind;
        }

        if (MatchesHost(host, "wikidata.org"))
        {
            return WikidataKind;
        }

        return type.Equals(DiscogsKind, StringComparison.OrdinalIgnoreCase) ||
            type.Equals(WikidataKind, StringComparison.OrdinalIgnoreCase)
                ? null
                : type;
    }

    private static bool MatchesHost(string host, string domain)
    {
        return string.Equals(host, domain, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
    }
}
