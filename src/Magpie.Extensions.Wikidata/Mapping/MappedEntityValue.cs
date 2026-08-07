namespace Magpie.Extensions.Wikidata.Mapping;

internal sealed record MappedEntityValue<T>(
    string EntityQid,
    T Value,
    int? SourceStatementOrdinal = null);
