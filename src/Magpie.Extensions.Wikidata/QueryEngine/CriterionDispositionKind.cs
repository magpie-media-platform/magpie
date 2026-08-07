namespace Magpie.Extensions.Wikidata.QueryEngine;

internal enum CriterionDispositionKind
{
    ServerFilter,
    Residual,
    ServerFilterAndResidual,
    ProvablyUnsatisfiable,
    Omitted,
    Refused
}
