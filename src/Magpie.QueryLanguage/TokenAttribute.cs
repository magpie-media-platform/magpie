using System;

namespace Magpie.QueryLanguage;

[AttributeUsage(AttributeTargets.Field)]
internal sealed class TokenAttribute(string text, TokenCategory category) : Attribute
{
    public string Text { get; } = text;

    public TokenCategory Category { get; } = category;
}
