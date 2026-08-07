using System;
using System.Collections.Generic;

using Magpie.Extensions.MusicBrainz.QueryEngine;

namespace Magpie.Extensions.MusicBrainz;

internal sealed record EntityCacheKey
{
    public required EntityKind Kind { get; init; }

    public required string Mbid { get; init; }

    public required LookupProfile Profile { get; init; }

    public LookupCapability Capabilities { get; init; }

    public LookupCapability EffectiveCapabilities
    {
        get
        {
            return Capabilities | ProfileCapabilities();
        }
    }

    public string RequestTarget
    {
        get
        {
            var entity = Kind switch
            {
                EntityKind.Artist => "artist",
                EntityKind.ReleaseGroup => "release-group",
                EntityKind.Release => "release",
                EntityKind.Recording => "recording",
                EntityKind.Label => "label",
                _ => string.Empty
            };
            var includes = EntityIncludes();
            return entity + "/" + Uri.EscapeDataString(Mbid) + "?fmt=json" +
                (includes.Count == 0 ? string.Empty : "&inc=" + string.Join("%2B", includes));
        }
    }

    public EntityCacheKey Normalize()
    {
        return this with { Capabilities = EffectiveCapabilities };
    }

    public bool Satisfies(EntityCacheKey requested)
    {
        return Kind == requested.Kind &&
            string.Equals(Mbid, requested.Mbid, StringComparison.OrdinalIgnoreCase) &&
            (int)Profile >= (int)requested.Profile &&
            (EffectiveCapabilities & requested.EffectiveCapabilities) == requested.EffectiveCapabilities;
    }

    private LookupCapability ProfileCapabilities()
    {
        if (Profile != LookupProfile.Hydration)
        {
            return LookupCapability.None;
        }

        return Kind switch
        {
            EntityKind.Artist =>
                LookupCapability.Aliases | LookupCapability.UrlRelations | LookupCapability.Genres |
                LookupCapability.ArtistRelations,
            EntityKind.ReleaseGroup =>
                LookupCapability.Releases | LookupCapability.UrlRelations | LookupCapability.Genres,
            EntityKind.Release => LookupCapability.Labels,
            EntityKind.Label => LookupCapability.UrlRelations,
            _ => LookupCapability.None
        };
    }

    private IReadOnlyList<string> EntityIncludes()
    {
        var includes = new List<string>();
        if (Profile == LookupProfile.Hydration)
        {
            AddProfileIncludes(includes, Kind);
        }
        else
        {
            AddSearchIncludes(includes, Kind);
        }

        AddCapabilityIncludes(includes, Kind, Capabilities);
        return includes;
    }

    private static void AddSearchIncludes(List<string> includes, EntityKind kind)
    {
        switch (kind)
        {
            case EntityKind.ReleaseGroup:
                AddInclude(includes, "artist-credits");
                break;
            case EntityKind.Release:
                AddInclude(includes, "release-groups");
                break;
            case EntityKind.Recording:
                AddInclude(includes, "artist-credits");
                AddInclude(includes, "isrcs");
                break;
        }
    }

    private static void AddProfileIncludes(List<string> includes, EntityKind kind)
    {
        switch (kind)
        {
            case EntityKind.Artist:
                AddInclude(includes, "aliases");
                AddInclude(includes, "genres");
                AddInclude(includes, "url-rels");
                AddInclude(includes, "artist-rels");
                break;
            case EntityKind.ReleaseGroup:
                AddInclude(includes, "releases");
                AddInclude(includes, "artist-credits");
                AddInclude(includes, "aliases");
                AddInclude(includes, "genres");
                AddInclude(includes, "url-rels");
                break;
            case EntityKind.Release:
                AddInclude(includes, "recordings");
                AddInclude(includes, "artist-credits");
                AddInclude(includes, "isrcs");
                AddInclude(includes, "media");
                AddInclude(includes, "labels");
                AddInclude(includes, "release-groups");
                AddInclude(includes, "artist-rels");
                AddInclude(includes, "recording-level-rels");
                AddInclude(includes, "work-rels");
                AddInclude(includes, "work-level-rels");
                break;
            case EntityKind.Recording:
                AddSearchIncludes(includes, kind);
                AddInclude(includes, "aliases");
                AddInclude(includes, "genres");
                AddInclude(includes, "url-rels");
                AddInclude(includes, "artist-rels");
                AddInclude(includes, "work-rels");
                AddInclude(includes, "work-level-rels");
                break;
            case EntityKind.Label:
                AddInclude(includes, "url-rels");
                break;
        }
    }

    private static void AddCapabilityIncludes(
        List<string> includes,
        EntityKind kind,
        LookupCapability capabilities)
    {
        if ((capabilities & LookupCapability.Aliases) != 0 &&
            kind is EntityKind.Artist or EntityKind.ReleaseGroup or EntityKind.Recording)
        {
            AddInclude(includes, "aliases");
        }

        if ((capabilities & LookupCapability.UrlRelations) != 0)
        {
            AddInclude(includes, "url-rels");
        }

        if ((capabilities & LookupCapability.Releases) != 0 &&
            kind is EntityKind.Artist or EntityKind.ReleaseGroup or EntityKind.Recording)
        {
            AddInclude(includes, "releases");
        }

        if ((capabilities & LookupCapability.ExternalIds) != 0 &&
            kind is EntityKind.ReleaseGroup or EntityKind.Recording)
        {
            AddInclude(includes, "releases");
        }

        if ((capabilities & LookupCapability.Labels) != 0 && kind == EntityKind.Release)
        {
            AddInclude(includes, "labels");
        }

        if ((capabilities & LookupCapability.Genres) != 0 &&
            kind is EntityKind.Artist or EntityKind.ReleaseGroup or EntityKind.Recording)
        {
            AddInclude(includes, "genres");
        }

        if ((capabilities & LookupCapability.ArtistRelations) != 0 && kind == EntityKind.Artist)
        {
            AddInclude(includes, "artist-rels");
        }

        if ((capabilities & LookupCapability.ReleaseMedia) != 0 && kind == EntityKind.Release)
        {
            AddInclude(includes, "recordings");
            AddInclude(includes, "media");
        }

        if ((capabilities & LookupCapability.ReleaseMediaSummary) != 0 && kind == EntityKind.ReleaseGroup)
        {
            AddInclude(includes, "releases");
            AddInclude(includes, "media");
        }

        if ((capabilities & LookupCapability.ReleaseGroups) != 0 && kind == EntityKind.Recording)
        {
            AddInclude(includes, "releases");
            AddInclude(includes, "release-groups");
        }
    }

    private static void AddInclude(List<string> includes, string include)
    {
        if (!includes.Exists(value => string.Equals(value, include, StringComparison.Ordinal)))
        {
            includes.Add(include);
        }
    }
}
