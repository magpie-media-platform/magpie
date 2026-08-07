using System;
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
            return Language!;
        }

        if (!string.IsNullOrWhiteSpace(defaultLanguagePreference))
        {
            return defaultLanguagePreference!;
        }

        return FallbackLanguage;
    }

    public static Settings Parse(IReadOnlyDictionary<string, string> settings)
    {
        var apiKey = TryGet(settings, ApiKeyKey);
        var language = TryGet(settings, LanguageKey);

        return new Settings
        {
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? string.Empty : apiKey!.Trim(),
            Language = string.IsNullOrWhiteSpace(language) ? null : language!.Trim()
        };
    }

    private static string? TryGet(IReadOnlyDictionary<string, string> settings, string key)
    {
        if (settings.TryGetValue(key, out var direct))
        {
            return direct;
        }

        foreach (var pair in settings)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }
}
