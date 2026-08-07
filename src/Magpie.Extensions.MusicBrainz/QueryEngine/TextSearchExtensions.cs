using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal static class TextSearchExtensions
{
    internal static IReadOnlyList<string> SearchFragments(this string value)
    {
        var fragments = new List<string>();
        var current = new StringBuilder();
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character) || char.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                current.Append(character);
            }
            else if (current.Length > 0)
            {
                fragments.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            fragments.Add(current.ToString());
        }

        return fragments;
    }
}
