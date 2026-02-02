namespace Magpie.Models.Sorting;

/// <summary>
/// Sorting parameters.
/// </summary>
/// <param name="PropertyName">The property name to sort by.</param>
/// <param name="Direction">The direction to sort in.</param>
public sealed record SortParameter(string PropertyName, SortDirection Direction);
