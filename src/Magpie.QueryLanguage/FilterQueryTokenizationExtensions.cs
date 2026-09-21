using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace Magpie.QueryLanguage;

internal static class FilterQueryTokenizationExtensions
{
    private static readonly TextParser<char> StringChar =
        Character.EqualTo('\\').IgnoreThen(Character.AnyChar)
            .Or(Character.Except(c => c is '"' or '\\', "string character"));

    private static readonly TextParser<TextSpan> QuotedString =
        Span.MatchedBy(
            Character.EqualTo('"')
                .IgnoreThen(StringChar.Many())
                .IgnoreThen(Character.EqualTo('"')));

    private static readonly TextParser<TextSpan> NegativeNumber =
        Span.MatchedBy(Character.EqualTo('-').IgnoreThen(Numerics.Decimal.Or(Numerics.Integer)));

    private static readonly TextParser<TextSpan> Number =
        NegativeNumber.Try().Or(Numerics.Decimal).Or(Numerics.Integer);

    private static readonly TextParser<TextSpan> Identifier =
        Span.MatchedBy(
            Character.Letter.Or(Character.EqualTo('_'))
                .IgnoreThen(Character.LetterOrDigit.Or(Character.EqualTo('_')).Many()));

    private static readonly Tokenizer<FilterQueryToken> Instance = BuildTokenizer();

    public static Result<TokenList<FilterQueryToken>> TryTokenize(this string input)
    {
        return Instance.TryTokenize(input);
    }

    private static Tokenizer<FilterQueryToken> BuildTokenizer()
    {
        var builder = new TokenizerBuilder<FilterQueryToken>()
            .Ignore(Span.WhiteSpace);

        foreach (var (token, text) in TokenCategory.Punctuation.GetTokens())
        {
            builder.Match(Character.EqualTo(text[0]), token);
        }

        foreach (var (token, text) in TokenCategory.Operator.GetTokens())
        {
            if (text.Length == 1)
            {
                builder.Match(Character.EqualTo(text[0]), token);
            }
            else
            {
                builder.Match(Span.EqualTo(text), token);
            }
        }

        foreach (var (token, text) in TokenCategory.NegatedKeyword.GetTokens())
        {
            builder.Match(Span.EqualToIgnoreCase(text), token, requireDelimiters: true);
        }

        foreach (var (token, text) in TokenCategory.Keyword.GetTokens())
        {
            builder.Match(Span.EqualToIgnoreCase(text), token, requireDelimiters: true);
        }

        builder
            .Match(QuotedString, FilterQueryToken.String)
            .Match(Number, FilterQueryToken.Number)
            .Match(Identifier, FilterQueryToken.Identifier);

        return builder.Build();
    }
}
