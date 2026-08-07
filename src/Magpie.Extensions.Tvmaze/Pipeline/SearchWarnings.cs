namespace Magpie.Extensions.Tvmaze.Pipeline;

internal static class SearchWarnings
{
    internal static readonly string Unbounded
        = "tvmaze advanced search requires a bounded identifier, title, alternate title, or person relationship seed;" +
        " no search was run.";

    internal static readonly string AkaSearchApproximate
        = "tvmaze alternate-title search is fuzzy and may omit matching shows.";

    internal static readonly string TitleSearchApproximate = "TVMaze's fixed unpaged ranked search may omit matches.";

    internal static readonly string PersonSearchApproximate
        = "tvmaze person-name search is fuzzy; TVMaze's fixed unpaged ranked search may omit matching identities.";

    internal static readonly string CreditVerificationUnavailable =
        "tvmaze could not load credits required to verify at least one candidate; that candidate was omitted.";

    internal static readonly string ImageVerificationUnavailable =
        "tvmaze could not load images required to verify at least one candidate; that candidate was omitted.";

    internal static readonly string AkasUnavailable
        = "tvmaze could not load alternate titles required to verify at least one candidate; that candidate was" +
        " omitted.";

    internal static readonly string ExecutionFailed
        = "tvmaze could not execute at least one required query-plan branch; results may be incomplete.";
}
