using Magpie.Extensions.Tmdb.Mapping;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record CreditResponse
{
    internal PersonMovieCreditsDto? Movie { get; init; }

    internal PersonTvCreditsDto? Tv { get; init; }
}
