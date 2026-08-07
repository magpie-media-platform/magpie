using System;
using System.Collections.Generic;
using System.Linq;

using Superpower.Model;

namespace Magpie.QueryLanguage;

internal static class FilterQueryParsingExtensions
{
    private const int MaxNestingDepth = 32;
    private const int MaxInputLength = 8192;
    private const int MaxOperatorCount = 256;

    public static ParseResult<QueryNode> ParseFilterQuery(this string query)
    {
        if (query.Length > MaxInputLength)
        {
            return ParseResult<QueryNode>.Failure(new(
                $"The query exceeds the limit of {MaxInputLength} characters.",
                MaxInputLength,
                []));
        }

        var tokenizeResult = query.TryTokenize();
        if (!tokenizeResult.HasValue)
        {
            return ParseResult<QueryNode>.Failure(new(
                tokenizeResult.FormatErrorMessageFragment(),
                tokenizeResult.ErrorPosition.Absolute,
                []));
        }

        var tokens = tokenizeResult.Value;
        if (tokens.IsAtEnd)
        {
            return ParseResult<QueryNode>.Failure(new("Empty query", 0, ["field expression"]));
        }

        var depthError = CheckNestingDepth(tokens);
        if (depthError != null)
        {
            return ParseResult<QueryNode>.Failure(depthError.Value);
        }

        var operatorError = CheckOperatorCount(tokens);
        if (operatorError != null)
        {
            return ParseResult<QueryNode>.Failure(operatorError.Value);
        }

        var parseResult = tokens.ParseQuery();
        if (!parseResult.HasValue)
        {
            var expected = parseResult.Expectations ?? [];
            var expectedStrings = expected.Select(e => FormatTokenName(e ?? "token")).ToList();

            int position;
            string errorMessage;

            if (parseResult.Remainder.IsAtEnd)
            {
                position = query.Length;
                errorMessage = FormatExpectedMessage(expectedStrings);
            }
            else
            {
                var nextToken = parseResult.Remainder.ConsumeToken().Value;
                var tokenText = nextToken.ToStringValue();
                position = nextToken.Span.Position.Absolute;

                if (expectedStrings.Contains(")"))
                {
                    errorMessage = "Expected ')'";
                }
                else if (IsEmptyValueListContext(expectedStrings, tokenText))
                {
                    errorMessage = "Empty value list";
                }
                else if (IsFieldNameContext(expectedStrings))
                {
                    errorMessage = $"Expected field name, got {tokenText}";
                }
                else if (expectedStrings.Count > 0)
                {
                    errorMessage = $"Expected {FormatExpected(expectedStrings)}, got {tokenText}";
                }
                else
                {
                    errorMessage = $"Unexpected token: {tokenText}";
                }
            }

            return ParseResult<QueryNode>.Failure(new ParseError(errorMessage, position, expectedStrings));
        }

        return ParseResult<QueryNode>.Success(parseResult.Value);
    }

    private static ParseError? CheckNestingDepth(TokenList<FilterQueryToken> tokens)
    {
        var depth = 0;
        foreach (var token in tokens)
        {
            if (token.Kind == FilterQueryToken.LeftParen)
            {
                depth++;
                if (depth > MaxNestingDepth)
                {
                    return new ParseError(
                        $"The query exceeds the limit of {MaxNestingDepth} parenthesis levels.",
                        token.Span.Position.Absolute,
                        []);
                }
            }
            else if (token.Kind == FilterQueryToken.RightParen && depth > 0)
            {
                depth--;
            }
        }

        return null;
    }

    private static ParseError? CheckOperatorCount(TokenList<FilterQueryToken> tokens)
    {
        var count = 0;
        foreach (var token in tokens)
        {
            if (token.Kind is FilterQueryToken.And or FilterQueryToken.Or)
            {
                count++;
                if (count > MaxOperatorCount)
                {
                    return new ParseError(
                        $"The query exceeds the limit of {MaxOperatorCount} 'and' and 'or' operators.",
                        token.Span.Position.Absolute,
                        []);
                }
            }
        }

        return null;
    }

    private static bool IsFieldNameContext(IReadOnlyList<string> expected)
    {
        return expected.Contains("identifier") &&
            (expected.Contains("not") || expected.Contains("(")) &&
            !expected.Contains("string") &&
            !expected.Contains("number");
    }

    private static bool IsEmptyValueListContext(IReadOnlyList<string> expected, string tokenText)
    {
        return tokenText == ")" &&
            expected.Contains("string") &&
            expected.Contains("number") &&
            expected.Contains("identifier");
    }

    private static string FormatTokenName(string tokenName)
    {
        if (Enum.TryParse<FilterQueryToken>(tokenName, ignoreCase: true, out var token))
        {
            return token.GetTokenText() ?? tokenName;
        }

        return tokenName;
    }

    private static string FormatExpectedMessage(IReadOnlyList<string> expected)
    {
        if (expected.Count == 0)
        {
            return "Unexpected end of input";
        }

        if (expected.Contains(")"))
        {
            return "Expected ')'";
        }

        if (expected.Contains("string") || expected.Contains("number") || expected.Contains("value"))
        {
            return "Expected value";
        }

        return $"Expected {FormatExpected(expected)}";
    }

    private static string FormatExpected(IReadOnlyList<string> expected)
    {
        if (expected.Count == 0)
        {
            return "token";
        }

        if (expected.Count == 1)
        {
            return expected[0];
        }

        return string.Join(", ", expected.Take(expected.Count - 1)) + " or " + expected[^1];
    }
}
