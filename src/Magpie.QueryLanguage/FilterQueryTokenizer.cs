using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace Magpie.QueryLanguage;

internal static class FilterQueryTokenizer
{
    private static readonly TextParser<char> EscapeSequence =
        from backslash in Character.EqualTo('\\')
        from escaped in Character.AnyChar
        select escaped switch
        {
            'n' => '\n',
            'r' => '\r',
            't' => '\t',
            '"' => '"',
            '\\' => '\\',
            _ => escaped,
        };

    private static readonly TextParser<char> StringChar =
        EscapeSequence.Or(Character.Except(c => c == '"' || c == '\\', "string character"));

    private static readonly TextParser<string> StringContent =
        StringChar.Many().Select(chars => new string(chars));

    private static readonly TextParser<TextSpan> QuotedString =
        from open in Character.EqualTo('"')
        from content in StringContent
        from close in Character.EqualTo('"')
        select new TextSpan(content);

    private static readonly TextParser<TextSpan> NegativeNumber =
        from sign in Character.EqualTo('-')
        from num in Numerics.Integer.Or(Numerics.Decimal)
        select num;

    private static readonly TextParser<TextSpan> Number =
        NegativeNumber.Try().Or(Numerics.Decimal).Or(Numerics.Integer);

    private static readonly TextParser<TextSpan> Identifier =
        Span.MatchedBy(
            Character.Letter.Or(Character.EqualTo('_'))
                .IgnoreThen(Character.LetterOrDigit.Or(Character.EqualTo('_')).Many()));

    public static Tokenizer<FilterQueryToken> Instance { get; } = BuildTokenizer();

    public static TokenList<FilterQueryToken> Tokenize(string input)
    {
        return Instance.Tokenize(input);
    }

    public static Result<TokenList<FilterQueryToken>> TryTokenize(string input)
    {
        return Instance.TryTokenize(input);
    }

    private static Tokenizer<FilterQueryToken> BuildTokenizer()
    {
        var builder = new TokenizerBuilder<FilterQueryToken>()
            .Ignore(Span.WhiteSpace);

        foreach (var (token, text) in FilterQueryTokenExtensions.GetTokensByCategory(TokenCategory.Punctuation))
        {
            builder.Match(Character.EqualTo(text[0]), token);
        }

        foreach (var (token, text) in FilterQueryTokenExtensions.GetTokensByCategory(TokenCategory.Operator))
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

        foreach (var (token, text) in FilterQueryTokenExtensions.GetTokensByCategory(TokenCategory.NegatedKeyword))
        {
            builder.Match(Span.EqualToIgnoreCase(text), token, requireDelimiters: true);
        }

        foreach (var (token, text) in FilterQueryTokenExtensions.GetTokensByCategory(TokenCategory.Keyword))
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
