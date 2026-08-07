using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Models.Enums;
using Magpie.Models.Filtering;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal static class CriterionMatchingExtensions
{
    public static bool MatchesString(
        this CriteriaValueOperator operation,
        IReadOnlyList<string> values,
        string? candidate)
    {
        if (operation == CriteriaValueOperator.IsNull)
        {
            return string.IsNullOrWhiteSpace(candidate);
        }

        if (operation == CriteriaValueOperator.IsNotNull)
        {
            return !string.IsNullOrWhiteSpace(candidate);
        }

        if (candidate is null)
        {
            return false;
        }

        return operation switch
        {
            CriteriaValueOperator.Equals or CriteriaValueOperator.In =>
                values.Any(value => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.ContainsString =>
                values.Any(value => candidate.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0),
            CriteriaValueOperator.ContainsWord => values.Any(value => ContainsWord(candidate, value)),
            CriteriaValueOperator.StartsWithString =>
                values.Any(value => candidate.StartsWith(value, StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.EndsWithString =>
                values.Any(value => candidate.EndsWith(value, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    public static bool MatchesTemporal(
        this CriteriaValueOperator operation,
        IReadOnlyList<TemporalValue> values,
        TemporalValue? candidate)
    {
        if (operation == CriteriaValueOperator.IsNull)
        {
            return candidate is null;
        }

        if (operation == CriteriaValueOperator.IsNotNull)
        {
            return candidate is not null;
        }

        return candidate is not null && candidate.Compare(operation, values);
    }

    public static bool MatchesDuration(
        this CriteriaValueOperator operation,
        IReadOnlyList<long> values,
        TimeSpan? candidate)
    {
        if (operation == CriteriaValueOperator.IsNull)
        {
            return candidate is null;
        }

        if (operation == CriteriaValueOperator.IsNotNull)
        {
            return candidate is not null;
        }

        if (candidate is null)
        {
            return false;
        }

        var milliseconds = Convert.ToInt64(
            Math.Round(candidate.Value.TotalMilliseconds, MidpointRounding.AwayFromZero));
        return operation switch
        {
            CriteriaValueOperator.Equals or CriteriaValueOperator.In => values.Contains(milliseconds),
            CriteriaValueOperator.GreaterThan => milliseconds > values[0],
            CriteriaValueOperator.GreaterThanOrEqual => milliseconds >= values[0],
            CriteriaValueOperator.LessThan => milliseconds < values[0],
            CriteriaValueOperator.LessThanOrEqual => milliseconds <= values[0],
            _ => false
        };
    }

    private static bool ContainsWord(string candidate, string value)
    {
        var start = 0;
        while (start <= candidate.Length - value.Length)
        {
            var index = candidate.IndexOf(value, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            var before = index == 0 || !char.IsLetterOrDigit(candidate[index - 1]);
            var end = index + value.Length;
            var after = end == candidate.Length || !char.IsLetterOrDigit(candidate[end]);
            if (before && after)
            {
                return true;
            }

            start = index + 1;
        }

        return false;
    }
}
