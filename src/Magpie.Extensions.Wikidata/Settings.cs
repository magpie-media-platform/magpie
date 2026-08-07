namespace Magpie.Extensions.Wikidata;

internal sealed record Settings
{
    public const string ContactKey = "contact";

    public const string LanguageKey = "language";

    public const string FallbackLanguage = "en";

    public const string DefaultContact = "magpie-metadata";

    public const string GroupName = "magpie.wikidata";

    public string? Contact { get; init; }

    public string? Language { get; init; }
}
