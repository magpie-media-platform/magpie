using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for events.
/// </summary>
public sealed record EventFilter
{
    /// <summary>
    /// Criteria for the name of the event.
    /// </summary>
    public PropertyCriterion<string>? EventName { get; init; }

    /// <summary>
    /// Criteria for the context values of the event.
    /// </summary>
    public DictionaryCriteria<PropertyCriterion<string>>? EventContext { get; init; }
}
