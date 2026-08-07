namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal enum QueryStrategy
{
    None,

    Lucene,

    ArtistLookup,

    ReleaseGroupLookup,

    ReleaseLookup,

    RecordingLookup,

    IsrcLookup
}
