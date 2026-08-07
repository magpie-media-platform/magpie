using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Magpie.Models.Enums;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal static class CriteriaMatcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public static CriteriaMatchResult Match(
        string? actual,
        CriteriaValueOperator valueOperator,
        IReadOnlyList<string> values,
        bool negate = false)
    {
        var interpreted = InterpretOperator(valueOperator, negate);
        var result = MatchPositive(actual, interpreted.Operator, values);
        return interpreted.Negate ? Invert(result) : result;
    }

    public static (CriteriaValueOperator Operator, bool Negate) InterpretOperator(
        CriteriaValueOperator valueOperator,
        bool negate)
    {
        var interpreted = valueOperator switch
        {
            CriteriaValueOperator.NotEquals => (CriteriaValueOperator.Equals, true),
            CriteriaValueOperator.NotIn => (CriteriaValueOperator.In, true),
            CriteriaValueOperator.NotContainsString => (CriteriaValueOperator.ContainsString, true),
            CriteriaValueOperator.NotStartsWithString => (CriteriaValueOperator.StartsWithString, true),
            CriteriaValueOperator.NotEndsWithString => (CriteriaValueOperator.EndsWithString, true),
            _ => (valueOperator, false)
        };
        return (interpreted.Item1, negate ^ interpreted.Item2);
    }

    private static CriteriaMatchResult MatchAny(string actual, IReadOnlyList<string> values)
    {
        return MatchAny(values, value => string.Equals(actual, value, StringComparison.OrdinalIgnoreCase));
    }

    private static CriteriaMatchResult MatchAny(
        IReadOnlyList<string> values,
        Func<string, bool> predicate)
    {
        if (values.Count == 0)
        {
            return CriteriaMatchResult.Unverifiable;
        }

        foreach (var value in values)
        {
            if (predicate(value))
            {
                return CriteriaMatchResult.Matched;
            }
        }

        return CriteriaMatchResult.Rejected;
    }

    private static bool ContainsWord(string actual, string value)
    {
        var start = 0;
        while (start <= actual.Length - value.Length)
        {
            var index = actual.IndexOf(value, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            var leftBoundary = index == 0 || !IsWordCharacter(actual[index - 1]);
            var rightIndex = index + value.Length;
            var rightBoundary = rightIndex == actual.Length || !IsWordCharacter(actual[rightIndex]);
            if (leftBoundary && rightBoundary)
            {
                return true;
            }

            start = index + 1;
        }

        return false;
    }

    private static CriteriaMatchResult Invert(CriteriaMatchResult result)
    {
        return result switch
        {
            CriteriaMatchResult.Matched => CriteriaMatchResult.Rejected,
            CriteriaMatchResult.Rejected => CriteriaMatchResult.Matched,
            _ => CriteriaMatchResult.Unverifiable
        };
    }

    private static CriteriaMatchResult Result(bool matched)
    {
        return matched ? CriteriaMatchResult.Matched : CriteriaMatchResult.Rejected;
    }

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    private static CriteriaMatchResult MatchPositive(
        string? actual,
        CriteriaValueOperator valueOperator,
        IReadOnlyList<string> values)
    {
        if (valueOperator == CriteriaValueOperator.IsNull)
        {
            return Result(string.IsNullOrEmpty(actual));
        }

        if (valueOperator == CriteriaValueOperator.IsNotNull)
        {
            return Result(!string.IsNullOrEmpty(actual));
        }

        if (string.IsNullOrEmpty(actual))
        {
            return CriteriaMatchResult.Unverifiable;
        }

        return valueOperator switch
        {
            CriteriaValueOperator.Equals or CriteriaValueOperator.In => MatchAny(actual, values),
            CriteriaValueOperator.ContainsString => MatchAny(
                values,
                value => actual.Contains(value, StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.ContainsWord => MatchAny(values, value => ContainsWord(actual, value)),
            CriteriaValueOperator.StartsWithString => MatchAny(
                values,
                value => actual.StartsWith(value, StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.EndsWithString => MatchAny(
                values,
                value => actual.EndsWith(value, StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.Regex => values.Count == 0
                ? CriteriaMatchResult.Unverifiable
                : MatchRegex(actual, values[0]),
            _ => CriteriaMatchResult.Unverifiable
        };
    }

    private static CriteriaMatchResult MatchRegex(string actual, string pattern)
    {
        try
        {
            return Result(Regex.IsMatch(
                actual,
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                RegexTimeout));
        }
        catch (ArgumentException)
        {
            return CriteriaMatchResult.Unverifiable;
        }
        catch (RegexMatchTimeoutException)
        {
            return CriteriaMatchResult.Unverifiable;
        }
    }
}
