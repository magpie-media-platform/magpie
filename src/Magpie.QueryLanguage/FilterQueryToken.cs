namespace Magpie.QueryLanguage;

/// <summary>
/// Token types for the filter query language (Superpower tokenizer).
/// </summary>
internal enum FilterQueryToken
{
    /// <summary>
    /// An identifier (field name, enum value, boolean).
    /// </summary>
    [Token("identifier", TokenCategory.Dynamic)]
    Identifier,

    /// <summary>
    /// A quoted string literal.
    /// </summary>
    [Token("string", TokenCategory.Dynamic)]
    String,

    /// <summary>
    /// A numeric literal.
    /// </summary>
    [Token("number", TokenCategory.Dynamic)]
    Number,

    /// <summary>
    /// Open parenthesis.
    /// </summary>
    [Token("(", TokenCategory.Punctuation)]
    LeftParen,

    /// <summary>
    /// Close parenthesis.
    /// </summary>
    [Token(")", TokenCategory.Punctuation)]
    RightParen,

    /// <summary>
    /// Comma separator.
    /// </summary>
    [Token(",", TokenCategory.Punctuation)]
    Comma,

    /// <summary>
    /// Equals operator.
    /// </summary>
    [Token("=", TokenCategory.Operator)]
    Equals,

    /// <summary>
    /// Not equals operator.
    /// </summary>
    [Token("!=", TokenCategory.Operator)]
    NotEquals,

    /// <summary>
    /// Greater than operator.
    /// </summary>
    [Token(">", TokenCategory.Operator)]
    GreaterThan,

    /// <summary>
    /// Greater than or equal operator.
    /// </summary>
    [Token(">=", TokenCategory.Operator)]
    GreaterThanOrEqual,

    /// <summary>
    /// Less than operator.
    /// </summary>
    [Token("<", TokenCategory.Operator)]
    LessThan,

    /// <summary>
    /// Less than or equal operator.
    /// </summary>
    [Token("<=", TokenCategory.Operator)]
    LessThanOrEqual,

    /// <summary>
    /// Keyword: and.
    /// </summary>
    [Token("and", TokenCategory.Keyword)]
    And,

    /// <summary>
    /// Keyword: or.
    /// </summary>
    [Token("or", TokenCategory.Keyword)]
    Or,

    /// <summary>
    /// Keyword: not.
    /// </summary>
    [Token("not", TokenCategory.Keyword)]
    Not,

    /// <summary>
    /// Keyword: has.
    /// </summary>
    [Token("has", TokenCategory.Keyword)]
    Has,

    /// <summary>
    /// Operator: !has.
    /// </summary>
    [Token("!has", TokenCategory.NegatedKeyword)]
    NotHas,

    /// <summary>
    /// Keyword: contains.
    /// </summary>
    [Token("contains", TokenCategory.Keyword)]
    Contains,

    /// <summary>
    /// Operator: !contains.
    /// </summary>
    [Token("!contains", TokenCategory.NegatedKeyword)]
    NotContains,

    /// <summary>
    /// Keyword: startswith.
    /// </summary>
    [Token("startswith", TokenCategory.Keyword)]
    StartsWith,

    /// <summary>
    /// Operator: !startswith.
    /// </summary>
    [Token("!startswith", TokenCategory.NegatedKeyword)]
    NotStartsWith,

    /// <summary>
    /// Keyword: endswith.
    /// </summary>
    [Token("endswith", TokenCategory.Keyword)]
    EndsWith,

    /// <summary>
    /// Operator: !endswith.
    /// </summary>
    [Token("!endswith", TokenCategory.NegatedKeyword)]
    NotEndsWith,

    /// <summary>
    /// Keyword: in.
    /// </summary>
    [Token("in", TokenCategory.Keyword)]
    In,

    /// <summary>
    /// Operator: !in.
    /// </summary>
    [Token("!in", TokenCategory.NegatedKeyword)]
    NotIn,

    /// <summary>
    /// Keyword: null.
    /// </summary>
    [Token("null", TokenCategory.Keyword)]
    Null,

    /// <summary>
    /// Operator: !null.
    /// </summary>
    [Token("!null", TokenCategory.NegatedKeyword)]
    NotNull,
}
