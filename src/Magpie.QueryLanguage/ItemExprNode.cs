namespace Magpie.QueryLanguage;

internal abstract record ItemExprNode
{
    public int Position { get; init; }
    public int Length { get; init; }
}
