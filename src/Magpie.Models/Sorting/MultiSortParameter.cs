using System.Collections.Generic;

namespace Magpie.Models.Sorting;

/// <summary>
/// Multiple sorting parameters.
/// </summary>
/// <param name="SortParameters">An priority ordered list of sorting parameters.</param>
public sealed record MultiSortParameter(IEnumerable<SortParameter> SortParameters);
