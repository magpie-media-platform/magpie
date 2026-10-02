using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Magpie.Data.Configuration;

internal static class PropertyBuilderExtensions
{
    private static readonly ValueConverter StringDictionaryConverter =
        new ValueConverter<Dictionary<string, string>, string>(
            dictionary => JsonSerializer.Serialize(dictionary, JsonSerializerOptions.Default),
            json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Default)
                ?? new());

    private static readonly ValueComparer<Dictionary<string, string>> StringDictionaryComparer =
        new(
            (left, right) => DictionariesEqual(left, right),
            dictionary => dictionary == null
                ? 0
                : dictionary.Aggregate(
                    0,
                    (hash, pair) => hash ^ HashCode.Combine(pair.Key, pair.Value)),
            dictionary => dictionary == null
                ? new()
                : new(dictionary));

    internal static PropertyBuilder<Dictionary<string, string>> UseStringDictionaryConversion(
        this PropertyBuilder<Dictionary<string, string>> builder)
    {
        builder.HasConversion(StringDictionaryConverter);
        builder.Metadata.SetValueComparer(StringDictionaryComparer);
        return builder;
    }

    internal static PropertyBuilder<Dictionary<string, string>?> UseNullableStringDictionaryConversion(
        this PropertyBuilder<Dictionary<string, string>?> builder)
    {
        builder.HasConversion(StringDictionaryConverter);
        builder.Metadata.SetValueComparer(StringDictionaryComparer);
        return builder;
    }

    internal static PropertyBuilder<List<T>> UseListComparer<T>(this PropertyBuilder<List<T>> builder)
    {
        builder.Metadata.SetValueComparer(
            new ValueComparer<List<T>>(
                (left, right) => ReferenceEquals(left, right) ||
                    (left != null && right != null && left.SequenceEqual(right)),
                list => list == null
                    ? 0
                    : list.Aggregate(
                        0,
                        (hash, item) => HashCode.Combine(hash, item == null ? 0 : item.GetHashCode())),
                list => list == null ? new() : new(list)));
        return builder;
    }

    internal static PropertyBuilder<T> UseJsonComparer<T>(this PropertyBuilder<T> builder)
    {
        builder.Metadata.SetValueComparer(
            new ValueComparer<T>(
                (left, right) => JsonSerializer.Serialize(left, JsonSerializerOptions.Default) ==
                    JsonSerializer.Serialize(right, JsonSerializerOptions.Default),
                value => value == null
                    ? 0
                    : JsonSerializer.Serialize(value, JsonSerializerOptions.Default)
                        .GetHashCode(StringComparison.Ordinal),
                value => value == null
                    ? default!
                    : JsonSerializer.Deserialize<T>(
                        JsonSerializer.Serialize(value, JsonSerializerOptions.Default),
                        JsonSerializerOptions.Default)!));
        return builder;
    }

    private static bool DictionariesEqual(
        Dictionary<string, string>? left,
        Dictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left == null || right == null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) ||
                !string.Equals(value, pair.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
