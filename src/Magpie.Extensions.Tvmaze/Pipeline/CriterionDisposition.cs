using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Pipeline;

internal sealed record CriterionDisposition(
    string Source,
    [property: JsonIgnore]
    object OperatorsAndValues,
    string Disposition,
    bool Negate = false);
