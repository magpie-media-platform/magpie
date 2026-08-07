namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal enum AnchorKind
{
    Mbid,

    Isrc,

    ResolvedArtist,

    FieldScoped,

    UnfieldedText,

    ReverseRelationship
}
