namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal enum FetchOutcome
{
    Success,

    NotFound,

    InvalidRequest,

    Unavailable,

    Timeout,

    Malformed,

    ResponseTooLarge
}
