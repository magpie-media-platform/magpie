using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Models.Enums;
using Magpie.Models.Filtering;

namespace Magpie.Extensions.Tvmaze.Pipeline;

internal static class CriterionGrammarExtensions
{
    public static (CriteriaValueOperator Operator, bool Negate) InterpretOperator(
        this CriteriaValueOperator op,
        bool negate)
    {
        var (primitive, negative) = op switch
        {
            CriteriaValueOperator.NotEquals => (CriteriaValueOperator.Equals, true),
            CriteriaValueOperator.NotIn => (CriteriaValueOperator.In, true),
            CriteriaValueOperator.NotContainsString => (CriteriaValueOperator.ContainsString, true),
            CriteriaValueOperator.NotStartsWithString => (CriteriaValueOperator.StartsWithString, true),
            CriteriaValueOperator.NotEndsWithString => (CriteriaValueOperator.EndsWithString, true),
            _ => (op, false)
        };
        var effective = negate ^ negative;
        return (primitive, effective) switch
        {
            (CriteriaValueOperator.IsNull, true) => (CriteriaValueOperator.IsNotNull, false),
            (CriteriaValueOperator.IsNotNull, true) => (CriteriaValueOperator.IsNull, false),
            _ => (primitive, effective)
        };
    }

    public static bool IsTextOperator(this CriteriaValueOperator op)
    {
        return op is CriteriaValueOperator.Equals
            or CriteriaValueOperator.In
            or CriteriaValueOperator.ContainsString
            or CriteriaValueOperator.ContainsWord
            or CriteriaValueOperator.StartsWithString
            or CriteriaValueOperator.EndsWithString
            or CriteriaValueOperator.IsNull
            or CriteriaValueOperator.IsNotNull;
    }

    public static bool IsOrderedOperator(this CriteriaValueOperator op)
    {
        return op is CriteriaValueOperator.Equals
            or CriteriaValueOperator.In
            or CriteriaValueOperator.GreaterThan
            or CriteriaValueOperator.GreaterThanOrEqual
            or CriteriaValueOperator.LessThan
            or CriteriaValueOperator.LessThanOrEqual
            or CriteriaValueOperator.IsNull
            or CriteriaValueOperator.IsNotNull;
    }

    public static string[] TextValues(this PropertyCriterion<string> criterion)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(criterion.Value))
        {
            values.Add(criterion.Value.Trim());
        }

        values.AddRange((criterion.Values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()));
        return [.. values.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    public static string[] UriValues(this PropertyCriterion<Uri> criterion)
    {
        var values = new List<string>();
        if (criterion.Value is not null)
        {
            values.Add(criterion.Value.ToString());
        }

        values.AddRange((criterion.Values ?? []).Where(value => value is not null).Select(value => value.ToString()));
        return [.. values.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    public static TemporalValue[] TemporalValues(this PropertyCriterion<TemporalValue> criterion)
    {
        var values = new List<TemporalValue>();
        if (criterion.Value is not null)
        {
            values.Add(criterion.Value);
        }

        values.AddRange((criterion.Values ?? []).Where(value => value is not null));
        return [.. values];
    }

    public static IReadOnlyList<TimeSpan> DurationValues(this PropertyCriterion<TimeSpan> criterion)
    {
        var values = (criterion.Values ?? []).ToList();
        if (criterion.Value != default)
        {
            values.Insert(0, criterion.Value);
        }

        return values;
    }

    public static IReadOnlyList<int> IntValues(this PropertyCriterion<int> criterion)
    {
        var values = (criterion.Values ?? []).ToList();
        if (criterion.Value != default || values.Count == 0)
        {
            values.Insert(0, criterion.Value);
        }

        return values;
    }

    public static bool MatchesText(this CriteriaValueOperator op, string? candidate, IReadOnlyList<string> values)
    {
        if (op == CriteriaValueOperator.IsNull)
        {
            return string.IsNullOrWhiteSpace(candidate);
        }

        if (op == CriteriaValueOperator.IsNotNull)
        {
            return !string.IsNullOrWhiteSpace(candidate);
        }

        if (candidate is null)
        {
            return false;
        }

        return op switch
        {
            CriteriaValueOperator.Equals or CriteriaValueOperator.In =>
                values.Any(value => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.ContainsString =>
                values.Any(value => candidate.Contains(value, StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.ContainsWord => values.Any(value => ContainsWord(candidate, value)),
            CriteriaValueOperator.StartsWithString =>
                values.Any(value => candidate.StartsWith(value, StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.EndsWithString =>
                values.Any(value => candidate.EndsWith(value, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    public static bool Matches(this PropertyCriterion<string> criterion, string? candidate)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        var matches = MatchesText(op, candidate, TextValues(criterion));
        return negate ? !matches : matches;
    }

    public static bool Matches(this PropertyCriterion<Uri> criterion, Uri? candidate)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        var matches = MatchesText(op, candidate?.ToString(), UriValues(criterion));
        return negate ? !matches : matches;
    }

    public static bool Matches(this PropertyCriterion<int> criterion, int? candidate)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        var values = IntValues(criterion);
        var matches = op switch
        {
            CriteriaValueOperator.IsNull => candidate is null,
            CriteriaValueOperator.IsNotNull => candidate is not null,
            CriteriaValueOperator.Equals or CriteriaValueOperator.In =>
                candidate is not null && values.Contains(candidate.Value),
            CriteriaValueOperator.GreaterThan =>
                candidate is not null && values.Count > 0 && candidate.Value > values[0],
            CriteriaValueOperator.GreaterThanOrEqual =>
                candidate is not null && values.Count > 0 && candidate.Value >= values[0],
            CriteriaValueOperator.LessThan =>
                candidate is not null && values.Count > 0 && candidate.Value < values[0],
            CriteriaValueOperator.LessThanOrEqual =>
                candidate is not null && values.Count > 0 && candidate.Value <= values[0],
            _ => false
        };
        return negate ? !matches : matches;
    }

    private static bool ContainsWord(string candidate, string value)
    {
        if (value.Length == 0 || value.Length > candidate.Length)
        {
            return false;
        }

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
