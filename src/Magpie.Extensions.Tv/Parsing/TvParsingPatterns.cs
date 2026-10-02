using System.Text.RegularExpressions;

namespace Magpie.Extensions.Tv.Parsing;

internal static class TvParsingPatterns
{
    public static readonly Regex SeasonEpisode = new(
        @"(?<![A-Za-z0-9])s(?<season>\d{1,3})[\s._-]*e(?<episode>\d{1,4})" +
        @"(?:(?:[\s._-]*e|-)(?<end>\d{1,4}))*(?![A-Za-z0-9])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static readonly Regex NumXNum = new(
        @"(?<![0-9])(?<season>\d{1,3})x(?<episode>\d{1,4})" +
        @"(?:(?:[\s._-]*\k<season>x|-)(?<end>\d{1,4}))*(?![A-Za-z0-9])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static readonly Regex Year = new(
        @"(?<![0-9])((19|20)\d{2})(?![0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
