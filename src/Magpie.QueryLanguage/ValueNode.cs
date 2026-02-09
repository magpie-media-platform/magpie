namespace Magpie.QueryLanguage;

internal abstract record ValueNode
{
    public int Position { get; init; }
    public int Length { get; init; }
}
