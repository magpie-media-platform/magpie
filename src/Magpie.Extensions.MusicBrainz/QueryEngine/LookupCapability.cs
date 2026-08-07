using System;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

[Flags]
internal enum LookupCapability
{
    None = 0,

    Aliases = 1 << 0,

    UrlRelations = 1 << 1,

    Releases = 1 << 2,

    Labels = 1 << 3,

    Genres = 1 << 4,

    ArtistRelations = 1 << 5,

    ExternalIds = 1 << 6,

    ReleaseMedia = 1 << 7,

    ReleaseGroups = 1 << 8,

    ReleaseMediaSummary = 1 << 9
}
