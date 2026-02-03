using Magpie.Models.Filtering;

namespace Magpie.Models.Patch;

/// <summary>
/// Patch for <see cref="DictionaryCriteria{TCriteria}"/>.
/// </summary>
/// <typeparam name="TCriteria">The criteria type for values in the dictionary.</typeparam>
/// <typeparam name="TCriteriaPatch">The criteria patch type for values in the dictionary.</typeparam>
public record DictionaryCriteriaPatch<TCriteria, TCriteriaPatch>
{
    /// <summary>
    /// The items criteria patch.
    /// </summary>
    public DictionaryPatch<string, TCriteria, TCriteriaPatch>? Items { get; init; }
}
