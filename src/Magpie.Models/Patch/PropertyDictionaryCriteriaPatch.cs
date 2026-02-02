using Magpie.Models.Filtering;

namespace Magpie.Models.Patch;

/// <summary>
/// Patch for <see cref="DictionaryCriteria{TCriteria}"/> with <see cref="PropertyCriterion{T}"/> values.
/// </summary>
/// <param name="Criteria">The criteria patch.</param>
public record PropertyDictionaryCriteriaPatch(
    DictionaryPatch<string, PropertyCriterion<string>, PropertyCriterionPatch>? Criteria = null) :
        DictionaryCriteriaPatch<PropertyCriterion<string>, PropertyCriterionPatch>(Criteria);
