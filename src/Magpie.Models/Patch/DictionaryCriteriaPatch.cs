using Magpie.Models.Filtering;

namespace Magpie.Models.Patch;

/// <summary>
/// Patch for <see cref="DictionaryCriteria{TCriteria}"/>.
/// </summary>
/// <typeparam name="TCriteria">The criteria type for values in the dictionary.</typeparam>
/// <typeparam name="TCriteriaPatch">The criteria patch type for values in the dictionary.</typeparam>
/// <param name="Criteria">The criteria patch.</param>
public record DictionaryCriteriaPatch<TCriteria, TCriteriaPatch>(
    DictionaryPatch<string, TCriteria, TCriteriaPatch>? Criteria = null);
