using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal static class ValueNormalizationExtensions
{
    private const string OpenLibrarySubjectKeyDroppedCharacters = ";/?:@&=+$,<>#%\"{}|\\^[]`\n\r";

    private static readonly IReadOnlyDictionary<string, string> LanguageNames = BuildLanguageNames();

    private static readonly IReadOnlyDictionary<string, string> LanguageAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = "eng",
            ["fr"] = "fre",
            ["de"] = "ger",
            ["es"] = "spa",
            ["it"] = "ita",
            ["pt"] = "por",
            ["ja"] = "jpn",
            ["zh"] = "chi",
            ["ru"] = "rus"
        };

    private static readonly IReadOnlyDictionary<string, string> BibliographicLanguageAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["alb"] = "alb",
            ["sqi"] = "alb",
            ["arm"] = "arm",
            ["hye"] = "arm",
            ["baq"] = "baq",
            ["eus"] = "baq",
            ["bur"] = "bur",
            ["mya"] = "bur",
            ["chi"] = "chi",
            ["zho"] = "chi",
            ["cze"] = "cze",
            ["ces"] = "cze",
            ["dut"] = "dut",
            ["nld"] = "dut",
            ["fre"] = "fre",
            ["fra"] = "fre",
            ["geo"] = "geo",
            ["kat"] = "geo",
            ["ger"] = "ger",
            ["deu"] = "ger",
            ["gre"] = "gre",
            ["ell"] = "gre",
            ["ice"] = "ice",
            ["isl"] = "ice",
            ["mac"] = "mac",
            ["mkd"] = "mac",
            ["may"] = "may",
            ["msa"] = "may",
            ["per"] = "per",
            ["fas"] = "per",
            ["rum"] = "rum",
            ["ron"] = "rum",
            ["slo"] = "slo",
            ["slk"] = "slo",
            ["tib"] = "tib",
            ["bod"] = "tib",
            ["wel"] = "wel",
            ["cym"] = "wel"
        };

    internal static string? NormalizeIsbn(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = new string([.. value!.Where(character => character is not ' ' and not '-')])
            .ToUpperInvariant();
        if (normalized.Length == 10)
        {
            var sum = 0;
            for (var index = 0; index < normalized.Length; index++)
            {
                var character = normalized[index];
                var digit = character == 'X' && index == 9 ? 10 : character - '0';
                if (digit is < 0 or > 9 && !(character == 'X' && index == 9))
                {
                    return null;
                }

                sum += digit * (10 - index);
            }

            return sum % 11 == 0 ? normalized : null;
        }

        if (normalized.Length == 13 && normalized.All(char.IsDigit))
        {
            var sum = 0;
            for (var index = 0; index < normalized.Length; index++)
            {
                sum += (normalized[index] - '0') * (index % 2 == 0 ? 1 : 3);
            }

            return sum % 10 == 0 ? normalized : null;
        }

        return null;
    }

    internal static string? NormalizeOlid(this string? value, char suffix)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
        if (normalized is null)
        {
            return null;
        }

        var slash = normalized.LastIndexOf('/');
        if (slash >= 0 && slash < normalized.Length - 1)
        {
            normalized = normalized[(slash + 1)..];
        }

        normalized = normalized.ToUpperInvariant();
        return normalized.Length >= 4 &&
            normalized[0] == 'O' &&
            normalized[1] == 'L' &&
            normalized[^1] == char.ToUpperInvariant(suffix) &&
            normalized[2..^1].All(char.IsDigit)
                ? normalized
                : null;
    }

    /// <summary>
    /// Builds the subject key that Open Library stores for a subject name.
    /// </summary>
    /// <remarks>
    /// A literal port of Open Library's <c>normalize_subject_name</c> (formerly <c>str_to_key</c>), which builds the
    /// search <c>*_key</c> fields, so it must not normalize further than Open Library does.
    /// </remarks>
    /// <param name="name">The subject name</param>
    internal static string ToOpenLibrarySubjectKey(this string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name.Trim().ToLowerInvariant())
        {
            if (character == ' ')
            {
                builder.Append('_');
            }
            else if (OpenLibrarySubjectKeyDroppedCharacters.IndexOf(character) < 0)
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    internal static string NormalizeSubjectKey(this string value)
    {
        var source = value.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
        var builder = new StringBuilder(source.Length);
        var separator = false;
        foreach (var character in source)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                separator = false;
            }
            else if (!separator && builder.Length > 0)
            {
                builder.Append('_');
                separator = true;
            }
        }

        return builder.ToString().TrimEnd('_');
    }

    internal static string? NormalizeLanguage(this string? value)
    {
        var normalized = NormalizeLanguageSubtag(value) ?? NormalizeLanguageName(value);
        if (normalized is null)
        {
            return null;
        }

        if (LanguageAliases.TryGetValue(normalized, out var alias))
        {
            return alias;
        }

        return BibliographicLanguageAliases.TryGetValue(normalized, out var bibliographic)
            ? bibliographic
            : normalized;
    }

    internal static string NormalizeLanguagePreference(this string? value)
    {
        var normalized = NormalizeLanguageSubtag(value);
        return normalized is { Length: 2 }
                ? normalized
                : string.Empty;
    }

    private static string? NormalizeLanguageSubtag(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var slash = normalized.LastIndexOf('/');
        if (slash >= 0 && slash < normalized.Length - 1)
        {
            normalized = normalized[(slash + 1)..];
        }

        var regionSeparator = normalized.IndexOfAny(['-', '_']);
        if (regionSeparator > 0)
        {
            normalized = normalized[..regionSeparator];
        }

        return normalized.Length is 2 or 3 &&
            normalized.All(character => character is >= 'a' and <= 'z')
                ? normalized
                : null;
    }

    private static string? NormalizeLanguageName(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : LanguageNames.TryGetValue(normalized!, out var code)
                ? code
                : null;
    }

    private static IReadOnlyDictionary<string, string> BuildLanguageNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.AllCultures))
        {
            var code = culture.ThreeLetterISOLanguageName.ToLowerInvariant();
            AddLanguageName(names, culture.Name, code);
            AddLanguageName(names, culture.IetfLanguageTag, code);
            AddLanguageName(names, culture.EnglishName, code);
            AddLanguageName(names, culture.NativeName, code);
        }

        return names;
    }

    private static void AddLanguageName(IDictionary<string, string> names, string? value, string code)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            names[value.Trim()] = code;
        }
    }
}
