using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Magpie.QueryLanguage;

internal static class FilterQueryTokenExtensions
{
    private static readonly Dictionary<FilterQueryToken, TokenAttribute> AttributeCache = BuildCache();

    private static Dictionary<FilterQueryToken, TokenAttribute> BuildCache()
    {
        var cache = new Dictionary<FilterQueryToken, TokenAttribute>();
        foreach (var field in typeof(FilterQueryToken).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var attr = field.GetCustomAttribute<TokenAttribute>();
            if (attr != null)
            {
                cache[(FilterQueryToken)field.GetValue(null)!] = attr;
            }
        }

        return cache;
    }

    public static string? GetTokenText(this FilterQueryToken token)
    {
        return AttributeCache.TryGetValue(token, out var attr) ? attr.Text : null;
    }

    internal static IReadOnlyList<(FilterQueryToken Token, string Text)> GetTokens(this TokenCategory category)
    {
        return AttributeCache
            .Where(kvp => kvp.Value.Category == category)
            .OrderByDescending(kvp => kvp.Value.Text.Length)
            .Select(kvp => (kvp.Key, kvp.Value.Text))
            .ToList();
    }
}
