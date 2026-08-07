using System;
using System.Collections.Generic;

namespace Magpie.Extensions.Tvmaze;

internal static class Constants
{
    internal static readonly Guid ExtensionId = new("055D58FA-9A32-42CA-A194-A3CE12C7E40E");

    public const string ProviderType = "tvmaze";

    public const string ImdbSource = "imdb";

    public const string TheTvdbSource = "tvdb";

    public const string TvRageSource = "tvrage";

    public const string ShowSourcePrefix = "tvmaze:show:";

    public const int CastCap = 30;

    public const int CrewCap = 30;

    public static IReadOnlyList<string> ShowImageTypes { get; } = ["poster", "banner", "background"];
}
