using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// Filter criteria for events.
/// </summary>
public sealed record EventFilter
{
    /// <summary>
    /// The criterion for matching the event name.
    /// </summary>
    public PropertyCriterion<string>? EventName { get; init; }

    /// <summary>
    /// The criteria for matching the event context.
    /// </summary>
    public PropertyDictionaryCriteria? EventContext { get; init; }
}
