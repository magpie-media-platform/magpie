using System.Collections.Generic;

namespace Magpie.Extensions.Tv.Parsing;

internal readonly record struct ParsedTvRelease(
    IReadOnlyList<int> Seasons,
    IReadOnlyList<int> Episodes,
    int? MarkerStart,
    IReadOnlyList<string> TitleTokens);
