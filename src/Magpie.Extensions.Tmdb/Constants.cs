using System;

namespace Magpie.Extensions.Tmdb;

internal static class Constants
{
    internal static readonly Guid ExtensionId = new("FE120126-EFD7-4DB1-A3A1-8B8A881E013A");

    public const string ProviderType = "tmdb";

    public const string DefaultImageBaseUrl = "https://image.tmdb.org/t/p/";

    public const string PersonSourcePrefix = ProviderType + ":person:";

    public const string Person = "person";

    public const string Cast = "cast";

    public const string Director = "director";

    public const string Writer = "writer";

    public const string Crew = "crew";

    public const string Studio = "studio";

    public const string Network = "network";
}
