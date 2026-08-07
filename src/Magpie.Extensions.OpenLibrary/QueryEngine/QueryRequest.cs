using Magpie.Models.Data;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed record QueryRequest
{
    public required QueryIntent Intent { get; init; }

    public required BookHierarchy Hierarchy { get; init; }

    public ContentHierarchyItemCriteria? Criteria { get; init; }

    internal MetadataCriteria? MetadataCriteria { get; init; }

    public string? Value { get; init; }

    public string? Source { get; init; }

    public string LanguagePreference { get; init; } = string.Empty;

    public int Page { get; init; } = 1;

    public int MaxResults { get; init; } = 20;
}
