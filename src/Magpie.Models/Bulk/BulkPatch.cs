namespace Magpie.Models.Bulk;

/// <summary>
/// A bulk operation that applies a patch to every selected item.
/// </summary>
/// <typeparam name="TFilter">The domain's query-filter type.</typeparam>
/// <typeparam name="TPatch">The patch type.</typeparam>
public sealed record BulkPatch<TFilter, TPatch> : BulkAction<TFilter>
    where TFilter : class
    where TPatch : class
{
    /// <summary>
    /// The patch applied to every selected item.
    /// </summary>
    public required TPatch Patch { get; init; }
}
