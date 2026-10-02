using System;
using System.Buffers;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Magpie.Core;

internal static class TextNormalizationExtensions
{
    private static readonly SearchValues<char> WindowsInvalidFileNameCharacters = SearchValues.Create("\\/:*?\"<>|");

    internal static string NormalizeContentPathSegment(this string source)
    {
        var trimmed = source.AsSpan().Trim();
        var segment = new char[trimmed.Length];
        var length = 0;
        var pendingSeparator = false;
        foreach (var character in trimmed)
        {
            if (character == ' ' || WindowsInvalidFileNameCharacters.Contains(character) || char.IsControl(character))
            {
                pendingSeparator = length > 0;
                continue;
            }

            if (pendingSeparator)
            {
                segment[length++] = ' ';
                pendingSeparator = false;
            }

            segment[length++] = character;
        }

        return new string(segment.AsSpan(0, length).TrimEnd('.'));
    }

    internal static string ToGeneratedContentPath(this string title, int? year)
    {
        var segment = title.NormalizeContentPathSegment();
        return year is null ? segment : $"{segment} ({year.Value.ToString(CultureInfo.InvariantCulture)})";
    }

    internal static string NormalizeExternalIdentifierName(this string name)
    {
        var stripped = new string([.. name.Where(char.IsLetterOrDigit)]).ToUpperInvariant();
        return stripped.Length > 2 && stripped.EndsWith("ID", StringComparison.Ordinal)
            ? stripped[..^2]
            : stripped;
    }

    internal static string NormalizeExternalIdentifierValue(this string value)
    {
        var trimmed = value.Trim();
        if (Guid.TryParse(trimmed, out var guid))
        {
            return guid.ToString("N", CultureInfo.InvariantCulture).ToUpperInvariant();
        }

        return long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : trimmed.ToUpperInvariant();
    }

    internal static string NormalizeContentSearchPhrase(this string? phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return string.Empty;
        }

        var decomposed = phrase.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (c is '\'' or '’')
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        var collapsed = new StringBuilder(builder.Length);
        var pendingSpace = false;
        foreach (var c in builder.ToString().Normalize(NormalizationForm.FormC))
        {
            if (c == ' ')
            {
                pendingSpace = collapsed.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                collapsed.Append(' ');
                pendingSpace = false;
            }

            collapsed.Append(c);
        }

        return collapsed.ToString();
    }
}
