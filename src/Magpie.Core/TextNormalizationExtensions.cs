using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Magpie.Core;

internal static class TextNormalizationExtensions
{
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
