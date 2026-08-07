using System;
using System.Collections.Generic;
using System.Linq;

namespace Magpie.Extensions.Wikidata.Mapping;

internal static class MonolingualTextMappingExtensions
{
    public static IReadOnlyList<MonolingualTextDto> Aliases(
        this IReadOnlyDictionary<string, List<MonolingualTextDto>>? values,
        string language)
        => Aliases(
            values,
            new[] { language, Settings.FallbackLanguage }.Distinct(StringComparer.OrdinalIgnoreCase),
            distinguishLanguages: false);

    public static IReadOnlyList<MonolingualTextDto> AllAliases(
        this IReadOnlyDictionary<string, List<MonolingualTextDto>>? values) => Aliases(
            values,
            values is null ? [] : values.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase),
            distinguishLanguages: true);

    private static IReadOnlyList<MonolingualTextDto> Aliases(
        IReadOnlyDictionary<string, List<MonolingualTextDto>>? values,
        IEnumerable<string> languages,
        bool distinguishLanguages)
    {
        if (values is null)
        {
            return [];
        }

        var result = new List<MonolingualTextDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in languages)
        {
            if (!values.TryGetValue(code, out var aliases))
            {
                continue;
            }

            foreach (var alias in aliases)
            {
                if (string.IsNullOrWhiteSpace(alias?.Value))
                {
                    continue;
                }

                var value = alias.Value.Trim();
                var identity = distinguishLanguages ? code + "\0" + value : value;
                if (seen.Add(identity))
                {
                    result.Add(new MonolingualTextDto { Language = code, Value = value });
                }
            }
        }

        return result;
    }

    public static IReadOnlyList<string> Names(this EntityDto entity, string language)
    {
        var names = new List<string>();
        var label = entity.Labels.Resolve(language);
        if (label is not null)
        {
            names.Add(label);
        }

        names.AddRange(entity.Aliases.Aliases(language).Select(alias => alias.Value!));
        return [.. names.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    public static string? Resolve(
        this IReadOnlyDictionary<string, MonolingualTextDto>? values,
        string language)
    {
        if (values is null)
        {
            return null;
        }

        if (values.TryGetValue(language, out var preferred) && !string.IsNullOrWhiteSpace(preferred?.Value))
        {
            return preferred.Value.Trim();
        }

        if (values.TryGetValue(Settings.FallbackLanguage, out var fallback) &&
            !string.IsNullOrWhiteSpace(fallback?.Value))
        {
            return fallback.Value.Trim();
        }

        return values.Values.Select(value => value?.Value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
