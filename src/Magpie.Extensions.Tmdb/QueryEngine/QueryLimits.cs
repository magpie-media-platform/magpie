namespace Magpie.Extensions.Tmdb.QueryEngine;

internal static class QueryLimits
{
    internal const int MaximumPage = 500;

    internal static readonly string PageLimitWarning
        = "TMDb reached its 500-page API limit. The results can omit additional matches.";
}
