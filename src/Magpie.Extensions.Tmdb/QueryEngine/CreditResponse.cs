using Magpie.Extensions.Tmdb.Mapping;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record CreditResponse
{
    internal PersonTvCreditsDto? Tv { get; init; }
}
