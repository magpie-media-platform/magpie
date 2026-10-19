# Metadata providers

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

A metadata provider fills in an existing [content item](media-types.md) or [subject](subject-types.md) with values from a source, such as an online catalog. Magpie stores those values as a named contribution, then combines contributions into [effective metadata](../../power-user/metadata.md#inspecting-a-conflicting-value). The provider supplies source data, and the host owns persistence and refresh work. You write one to add a metadata source.

To find candidates before adding them, write a [metadata searcher](metadata-search.md) instead. Search and enrichment can use the same catalog, but they take different inputs and return different results.

## Following a content refresh

A refresh selects providers whose `SupportedContentTypes` contains the item's content type slug or `"*"`. An empty list matches no types. The host calls `TryGetMetadataAsync` with a `MetadataFetchRequest`, which holds the item, existing contributions, ancestor metadata, and ancestor language preferences. Magpie sends an empty `DefaultLanguagePreference`, so use `AncestorLanguagePreferences` when it has values and the source's default language otherwise.

An external ID pairs a source name with that source's record identifier. Use existing external IDs to find the remote record, and for a child item, the ancestor metadata can supply the parent's identity. Prefer these to a title match, which can pick a different edition.

The returned `ContentHierarchyMetadataResult` has two parts. `Contribution` is metadata for the requested item, and `Children` describes its immediate children, with optional nested definitions. Magpie calls all matching providers at the same time in one service scope, so don't share state that isn't thread-safe between them. Manual edits are stored under the provider type `user`, so never use `user` as a `ProviderType`.

Set each child definition's `Level` to a direct child level of the refreshed item in the [content type's hierarchy](media-types.md#modeling-a-hierarchy-and-its-paths). The host skips other levels with a warning. It matches an existing child by `AlignmentKey`, then by external ID, then by level and slug. Keep those values stable, or a refresh can duplicate a child or update the wrong one.

Magpie also calls `TryGetMetadataAsync` when a user adds a [search candidate](metadata-search.md) whose `ProviderType` matches the provider. It uses the [last registered](README.md#registration-order) provider with that `ProviderType` that supports the content type. The item isn't saved yet, and `ExistingMetadata` holds one contribution with the selected external ID. Return a contribution for that ID. If the provider returns null, the add fails.

The return portion of a content provider's `TryGetMetadataAsync` looks like this. It needs `Magpie.Models` and `Magpie.Models.Data`, and `title` comes from a confirmed remote record.

```csharp
cancellationToken.ThrowIfCancellationRequested();
return new ContentHierarchyMetadataResult
{
    Contribution = new MetadataContribution
    {
        ProviderType = ProviderType,
        Source = "example-catalog",
        Title = title
    },
    Children = []
};
```

The [TMDB content provider](../../../src/Magpie.Extensions.Tmdb/ContentMetadataProvider.cs) shows the lookup, transport, and field mapping around it.

Return `null` only when the source has no record for the item. If the request fails, throw and preserve cancellation. Magpie logs the failure and applies the other providers' results, whereas an empty contribution that hides a failure can overwrite good values. If a provider failed and either no provider returned anything or a new item still has no title, the job fails and is retried. A null result alone doesn't cause a retry.

## Registering content and subject providers

Register each base contract in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point). Both interfaces are in `Magpie.Sdk.Content`.

```csharp
services.AddScoped<IContentHierarchyMetadataProvider, CatalogMetadataProvider>();
services.AddScoped<ISubjectMetadataProvider, CatalogSubjectProvider>();
```

[Scoped](../using-built-in-services.md#service-lifetime-and-ownership) providers can use settings, HTTP clients, and other [built-in services](../using-built-in-services.md). Pass cancellation to remote requests.

Every applicable provider contributes, and a new provider doesn't replace the others. Keep `ProviderType` unique and use the same value in the contribution, because reusing another provider's identifier can replace that provider's stored contribution.

`ISubjectMetadataProvider` receives a subject, existing contributions, and an empty language preference. `SupportedSubjectKinds` selects the kinds it enriches, with `"*"` for all, and it returns a `MetadataContribution` directly with no children. Magpie skips a contribution with a blank `ProviderType`. If a subject provider throws, Magpie logs it and continues with the others, and the subject refresh isn't retried.

To supply related people or organizations, implement the [relationship capability](metadata-relationships.md) on the content provider itself.

Contracts: [content provider](../../../src/Magpie.Sdk/Content/IContentHierarchyMetadataProvider.cs), [fetch request](../../../src/Magpie.Models/MetadataFetchRequest.cs), [result](../../../src/Magpie.Models/ContentHierarchyMetadataResult.cs), [child definition](../../../src/Magpie.Models/ChildHierarchyDefinition.cs), and [subject provider](../../../src/Magpie.Sdk/Content/ISubjectMetadataProvider.cs).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
