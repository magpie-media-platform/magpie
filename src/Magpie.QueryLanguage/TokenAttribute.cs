using System;

namespace Magpie.QueryLanguage;

[AttributeUsage(AttributeTargets.Field)]
internal sealed class TokenAttribute : Attribute
{
    public string Text { get; }

    public TokenCategory Category { get; }

    public TokenAttribute(string text, TokenCategory category)
    {
        Text = text;
        Category = category;
    }
}
