using System.Text.RegularExpressions;

namespace Magpie.Extensions.Music;

internal static class YearTokenExtensions
{
    private static readonly Regex Pattern = new(
        @"(?<![0-9])((19|20)\d{2})(?![0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static MatchCollection FindYearTokens(this string value) => Pattern.Matches(value);
}
