namespace Magpie.Models.Filtering;

/// <summary>
/// A <see cref="DictionaryCriteria{TCriteria}"/> with <see cref="PropertyCriterion{T}"/> values.
/// </summary>
public record PropertyDictionaryCriteria : DictionaryCriteria<PropertyCriterion<string>>
{
}
