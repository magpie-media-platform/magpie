namespace Magpie.Extensions.Wikidata.Mapping;

internal sealed record AwardCandidate(
    string EntityQid,
    int? Year,
    int SourceStatementOrdinal,
    StatementDto Statement);
