namespace Magpie.Extensions.Tv.Parsing;

internal static class TvParsingMessages
{
    internal const string AnimeAbsoluteNumberingReason =
        "anime-style absolute episode numbering ([Group] Show - 01) cannot be represented as parsed signals";

    internal const string DatedEpisodeReason =
        "dated episode shapes (Show - YYYY-MM-DD) cannot be represented as parsed signals";

    internal const string EmptyTitleReason = "no title survived normalisation";

    internal const string NoSeasonEpisodeReason = "no SxxEyy or NxNN signal was recognised";
}
