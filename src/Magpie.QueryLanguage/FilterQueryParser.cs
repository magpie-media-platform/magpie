using System;
using System.Collections.Generic;
using System.Linq;

namespace Magpie.QueryLanguage;

/// <summary>
/// Parses filter query strings into AST nodes.
/// </summary>
public static class FilterQueryParser
{
    /// <summary>
    /// Parses a filter query string into an AST.
    /// </summary>
    /// <param name="query">The query string to parse.</param>
    public static ParseResult<QueryNode> Parse(string query)
    {
        var tokenizeResult = FilterQueryTokenizer.TryTokenize(query);
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
            return ParseResult<QueryNode>.Failure(new(
                "Empty query",
                0,
                ["field expression"]));
        }

        var parseResult = FilterQueryParsers.Query(tokens);
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
                    errorMessage = $"Expected field name, got '{tokenText}'";
                }
                else if (expectedStrings.Count > 0)
                {
                    errorMessage = $"Expected {FormatExpected(expectedStrings)}, got '{tokenText}'";
                }
                else
                {
                    errorMessage = $"Unexpected token: '{tokenText}'";
                }
            }

            return ParseResult<QueryNode>.Failure(new ParseError(
                errorMessage,
                position,
                expectedStrings));
        }

        return ParseResult<QueryNode>.Success(parseResult.Value);
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
