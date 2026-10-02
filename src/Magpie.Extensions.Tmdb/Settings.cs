using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb;

internal sealed record Settings
{
    public const string ApiKeyKey = "apiKey";

    public const string LanguageKey = "language";

    public const string FallbackLanguage = "en-US";

    public const string GroupName = "magpie.tmdb";

    public string ApiKey { get; init; } = string.Empty;

    public string? Language { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public string ResolveLanguage(string? defaultLanguagePreference)
    {
        if (!string.IsNullOrWhiteSpace(Language))
        {
            return Language;
        }

        if (!string.IsNullOrWhiteSpace(defaultLanguagePreference))
        {
            return defaultLanguagePreference;
        }

        return FallbackLanguage;
    }

    public static Settings Parse(IReadOnlyDictionary<string, string> settings)
    {
        var apiKey = settings.GetValueOrDefault(ApiKeyKey);
        var language = settings.GetValueOrDefault(LanguageKey);

        return new Settings
        {
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? string.Empty : apiKey.Trim(),
            Language = string.IsNullOrWhiteSpace(language) ? null : language.Trim()
        };
    }
}
