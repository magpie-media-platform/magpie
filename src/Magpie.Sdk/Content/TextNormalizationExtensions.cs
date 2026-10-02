using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Magpie.Sdk.Content;

/// <summary>
/// Text normalization shared by content-type and metadata provider extensions.
/// </summary>
public static class TextNormalizationExtensions
{
    private static readonly Regex HtmlTagPattern = new("<[^>]*>", RegexOptions.CultureInvariant);

    private static readonly Regex WhitespaceRunPattern = new("\\s+", RegexOptions.CultureInvariant);

    private static readonly Lazy<IReadOnlyDictionary<string, string>> TwoLetterCodes
        = new(() => BuildLanguageCodes(static culture => culture.TwoLetterISOLanguageName));

    private static readonly Lazy<IReadOnlyDictionary<string, string>> ThreeLetterCodes
        = new(() => BuildLanguageCodes(static culture => culture.ThreeLetterISOLanguageName));

    /// <summary>
    /// Trims text and converts blank text to null.
    /// </summary>
    /// <param name="value">The text to inspect</param>
    public static string? NullIfBlank(this string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Normalizes a file name or folder name.
    /// </summary>
    /// <param name="input">The file name or folder name to normalize</param>
    public static string NormalizeMediaFilename(this string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var chars = new char[input.Length];
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            chars[i] = c switch
            {
                '.' or '_' or '(' or ')' or '[' or ']' or '{' or '}' => ' ',
                _ => c
            };
        }

        return WhitespaceRunPattern.Replace(new string(chars), " ").Trim();
    }

    /// <summary>
    /// Converts HTML markup to plain text.
    /// </summary>
    /// <param name="value">The markup to convert to plain text</param>
    public static string? NormalizeHtmlToText(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = WebUtility.HtmlDecode(HtmlTagPattern.Replace(value.Trim(), " "));
        text = WhitespaceRunPattern.Replace(text, " ").Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Resolves any culture name, tag, or English/native language name to its ISO 639-1 two-letter code.
    /// </summary>
    /// <param name="value">The language name or code to resolve</param>
    public static string? NormalizeTwoLetterLanguageCode(this string? value)
    {
        return LookupLanguageCode(TwoLetterCodes.Value, value);
    }

    /// <summary>
    /// Resolves any culture name, tag, or English/native language name to its ISO 639-2 three-letter code.
    /// </summary>
    /// <param name="value">The language name or code to resolve</param>
    public static string? NormalizeThreeLetterLanguageCode(this string? value)
    {
        return LookupLanguageCode(ThreeLetterCodes.Value, value);
    }

    private static string? LookupLanguageCode(IReadOnlyDictionary<string, string> codes, string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : codes.TryGetValue(normalized, out var code)
                ? code
                : null;
    }

    private static IReadOnlyDictionary<string, string> BuildLanguageCodes(Func<CultureInfo, string> selector)
    {
        var codes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.AllCultures))
        {
            var code = selector(culture).ToLowerInvariant();
            Add(codes, culture.Name, code);
            Add(codes, culture.IetfLanguageTag, code);
            Add(codes, culture.TwoLetterISOLanguageName, code);
            Add(codes, culture.ThreeLetterISOLanguageName, code);
            Add(codes, culture.EnglishName, code);
            Add(codes, culture.NativeName, code);
        }

        return codes;
    }

    private static void Add(IDictionary<string, string> codes, string? value, string code)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            codes[value.Trim()] = code;
        }
    }
}
