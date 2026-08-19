namespace Magpie.Extensions.Wikidata.QueryEngine;

internal static class TerminalReason
{
    internal const string Completed = "completed";

    internal const string KnownEmpty = "known_empty";

    internal const string Unbounded = "unbounded";

    internal const string IdentityEmpty = "identity_empty";

    internal const string RateLimited = "rate_limited";

    internal const string Timeout = "timeout";

    internal const string Cancelled = "cancelled";

    internal const string Error = "error";
}
