# Metadata search

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

A metadata searcher finds content outside the [local collection](../../using/collection.md), in an external catalog. Magpie shows its candidates in [**Add content**](../../using/collection.md#adding-a-title) and uses them for [wishlist discovery](../../using/monitoring-and-wishlists.md#creating-and-verifying-a-wishlist). A candidate points to a possible source item. It is separate from local collection records and from [downloaded releases](../../using/search-and-download.md). You write a searcher to make a catalog searchable.

**Add content** and wishlist discovery need `IContentHierarchyMetadataAdvancedSearcher`. A searcher that implements only `IContentHierarchyMetadataSearcher` supports text search through [`IMetadataSearchService.SearchAsync`](../using-built-in-services.md#reading-collection-data-and-metadata), which [library import](../../using/files.md#importing-existing-files) uses to match file names, but it produces no candidates for either discovery path.

## Text search and candidate identity

`IContentHierarchyMetadataSearcher` declares `ProviderType`, `SupportedContentTypes`, and `SearchAsync`. List each supported content type slug, or use `"*"` for all. An empty list matches no types.

The text-search method calls each matching searcher's `SearchAsync` once per root [hierarchy level](media-types.md#modeling-a-hierarchy-and-its-paths), with that level, the search text, an empty language preference, and a 1-based page number. Several providers can contribute candidates.

Return candidates with a stable `ProviderType`, `ExternalIdSource`, `ExternalId`, and `ContentType`, and populate `Level` when it is needed to tell searchable levels apart. Display text helps the user choose, while the [external identity](metadata-providers.md#following-a-content-refresh) lets later operations find the exact record. Set the candidate's `ProviderType` to that of a [metadata provider](metadata-providers.md) that supports the same content type, because Magpie calls that provider to read the initial metadata when the user adds the candidate. Without one, the add fails.

The [TMDB searcher](../../../src/Magpie.Extensions.Tmdb/ContentMetadataSearcher.cs) shows source requests, candidate mapping, and structured search. Follow your own source's paging and identity rules rather than copying TMDB's.

Register the searcher in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point). The contract is in `Magpie.Sdk.Content`.

```csharp
services.AddScoped<IContentHierarchyMetadataSearcher, CatalogSearcher>();
```

A [scoped](../using-built-in-services.md#service-lifetime-and-ownership) searcher can use [settings](../using-built-in-services.md#reading-configuration-and-application-state) and [HTTP services](../using-built-in-services.md#using-shared-facilities). Pass cancellation through its remote work.

## Structured criteria

Implement `IContentHierarchyMetadataAdvancedSearcher` on the same class, and keep registering through the base search interface. The host finds this [capability](../../reference/extensions.md#optional-capabilities) on that object.

`ContinueSearchAsync` receives the content type, structured criteria, a batch limit, an empty language preference, and an optional [continuation](#keeping-continuation-reusable). Magpie calls it for every batch, including the first, and expects verified candidates, warnings, and paging state back. For a candidate that lacks a value the criteria need, Magpie calls `HydrateAsync`, and if that throws, it adds a warning that the search is incomplete.

If the source can't enforce a criterion, check it against the data you have or report that the search isn't supported. A searcher that ignores a criterion returns results that don't match what the user asked for. For example, if a source can't tell a title's language, don't return every title as a match. Return an unsupported page with a warning, which Magpie treats as exhausted and shows only its warnings:

```csharp
return new MetadataSearchBatch
{
    Page = MetadataSearchPage.NotSupported with
    {
        Warnings = ["The catalog cannot filter by language."]
    },
    Continuation = null
};
```

This needs `Magpie.Sdk.Content` and `Magpie.Models.MetadataSearch`, and covers only the unsupported branch. Use `Page.Warnings` for failed or incomplete searches and accurate `PageInfo` for source exhaustion. An empty complete result means the source found no verified candidates.

Magpie requests batches of 25 candidates and fills a page in up to 4 rounds. After three failed searches for one content type, it skips the searcher for two minutes and shows a warning.

## Keeping continuation reusable

A continuation is a provider-owned checkpoint for the next batch. It can't depend on a scoped searcher staying alive, and it must not mutate or consume the one the caller supplies. If batch A returns checkpoint B, repeating the request with B must not skip rows because an earlier call advanced a shared cursor. Return a new checkpoint for the next position.

A `maxResults` of zero validates the checkpoint without consuming source rows. Reject an invalid checkpoint with `MetadataSearchContinuationException`, and propagate cancellation and source failures instead of reporting them as exhaustion.

See [text search](../../../src/Magpie.Sdk/Content/IContentHierarchyMetadataSearcher.cs), [advanced search](../../../src/Magpie.Sdk/Content/IContentHierarchyMetadataAdvancedSearcher.cs), [batch](../../../src/Magpie.Sdk/Content/MetadataSearchBatch.cs), and [candidate fields](../../../src/Magpie.Models/MetadataSearch/MetadataSearchCandidate.cs).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
