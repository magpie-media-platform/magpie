using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Models.Releases;

namespace Magpie.Core.Releases;

internal static class ContentFactExtensions
{
    internal static bool ContentEquals(this ContentFact left, ContentFact right) =>
        string.Equals(left.Value, right.Value, StringComparison.Ordinal) &&
        FacetsEqual(left.Facets, right.Facets);

    private static bool FacetsEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count &&
        left.All(pair => right.TryGetValue(pair.Key, out var value) &&
            string.Equals(pair.Value, value, StringComparison.Ordinal));
}
