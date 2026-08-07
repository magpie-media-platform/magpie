using System.Collections.Generic;
using System.Text.Json.Serialization;

using Magpie.Models.Filtering;

namespace Magpie.Extensions.Tvmaze.Pipeline;

[JsonDerivedType(typeof(IdLookup), "idLookup")]
[JsonDerivedType(typeof(FuzzyShow), "fuzzyShow")]
[JsonDerivedType(typeof(PersonChain), "personChain")]
[JsonDerivedType(typeof(Browse), "browse")]
internal abstract record SearchSeed
{
    internal sealed record IdLookup(IReadOnlyList<Lookup> Lookups) : SearchSeed;

    internal sealed record FuzzyShow(IReadOnlyList<string> Terms, bool IsAka) : SearchSeed;

    internal sealed record PersonChain(IReadOnlyList<PersonAlternative> Alternatives) : SearchSeed;

    internal sealed record Browse : SearchSeed;

    internal sealed record Lookup(string Source, string Value, int? ShowId);

    internal sealed record PersonAlternative(
        IReadOnlyList<string> Names,
        [property: JsonIgnore]
        IReadOnlyList<PropertyCriterion<string>> SubjectCriteria,
        IReadOnlyList<long> PersonIds,
        bool IncludeCast,
        bool IncludeCrew,
        IReadOnlyList<string> CrewKinds);
}
