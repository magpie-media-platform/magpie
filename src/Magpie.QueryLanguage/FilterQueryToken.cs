namespace Magpie.QueryLanguage;

internal enum FilterQueryToken
{
    [Token("identifier", TokenCategory.Dynamic)]
    Identifier,

    [Token("string", TokenCategory.Dynamic)]
    String,

    [Token("number", TokenCategory.Dynamic)]
    Number,

    [Token("(", TokenCategory.Punctuation)]
    LeftParen,

    [Token(")", TokenCategory.Punctuation)]
    RightParen,

    [Token(",", TokenCategory.Punctuation)]
    Comma,

    [Token("=", TokenCategory.Operator)]
    Equals,

    [Token("!=", TokenCategory.Operator)]
    NotEquals,

    [Token(">", TokenCategory.Operator)]
    GreaterThan,

    [Token(">=", TokenCategory.Operator)]
    GreaterThanOrEqual,

    [Token("<", TokenCategory.Operator)]
    LessThan,

    [Token("<=", TokenCategory.Operator)]
    LessThanOrEqual,

    [Token("and", TokenCategory.Keyword)]
    And,

    [Token("or", TokenCategory.Keyword)]
    Or,

    [Token("not", TokenCategory.Keyword)]
    Not,

    [Token("has", TokenCategory.Keyword)]
    Has,

    [Token("!has", TokenCategory.NegatedKeyword)]
    NotHas,

    [Token("contains", TokenCategory.Keyword)]
    Contains,

    [Token("!contains", TokenCategory.NegatedKeyword)]
    NotContains,

    [Token("startswith", TokenCategory.Keyword)]
    StartsWith,

    [Token("!startswith", TokenCategory.NegatedKeyword)]
    NotStartsWith,

    [Token("endswith", TokenCategory.Keyword)]
    EndsWith,

    [Token("!endswith", TokenCategory.NegatedKeyword)]
    NotEndsWith,

    [Token("in", TokenCategory.Keyword)]
    In,

    [Token("!in", TokenCategory.NegatedKeyword)]
    NotIn,

    [Token("null", TokenCategory.Keyword)]
    Null,

    [Token("!null", TokenCategory.NegatedKeyword)]
    NotNull,
}
