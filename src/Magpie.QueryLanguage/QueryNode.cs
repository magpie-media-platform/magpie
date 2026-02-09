namespace Magpie.QueryLanguage;

/// <summary>
/// Base type for all query AST nodes.
/// </summary>
public abstract record QueryNode
{
    /// <summary>
    /// The position of this node in the source query string.
    /// </summary>
    public int Position { get; init; }

    /// <summary>
    /// The length of this node in the source query string.
    /// </summary>
    public int Length { get; init; }
}
