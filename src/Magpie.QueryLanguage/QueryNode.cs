namespace Magpie.QueryLanguage;

internal abstract record QueryNode
{
    public int Position { get; init; }

    public int Length { get; init; }
}
