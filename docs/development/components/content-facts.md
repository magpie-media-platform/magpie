# Content facts

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

A [release search](../../using/search-and-download.md) has to identify what it is looking for. A show title alone does not identify one episode. A content fact provider supplies the extra facts for a target, such as a title combined with a season and episode number, or an [external identifier](metadata-providers.md#following-a-content-refresh). You write one when a media type needs searches that the host's title and external ID facts cannot express. Implement `IContentFactProvider`.

The host already builds facts from a target's titles and external IDs. It calls `CanProvideFacts` on each registered provider and adds the groups from every provider that applies, so an extension adds alternatives and removes none.

## Fact groups

`GetFactsAsync` receives a `ContentTarget`. Its `Item` is the target, `Lineage` is the [hierarchy](media-types.md#modeling-a-hierarchy-and-its-paths) from target to root, and `Metadata` holds available values by item ID. Match lineage items by level, because a title in the lineage can belong to an ancestor such as the show.

The outer list holds alternative searches, and each inner list holds facts that belong together. One episode described by a show title plus an episode code is a single group:

```csharp
IReadOnlyList<IReadOnlyList<ContentFact>> facts =
[
    [
        new() { Value = "Example Show" },
        new()
        {
            Value = "S02E03",
            Facets = new Dictionary<string, string>
            {
                ["season"] = "2",
                ["episode"] = "3"
            }
        }
    ]
];
return Task.FromResult(facts);
```

Split into two groups, **Example Show** and **S02E03** would be independent alternatives, and neither identifies the episode. The values here are fixed to show the shape, so derive them from the target in a real provider and return no groups when the identity is missing. `ContentFact` is in `Magpie.Models.Releases`.

Text supports keyword searches, and facets carry structured values for [indexers](indexers.md) that accept them. [Indexer capabilities](indexers.md#declaring-only-executable-searches) decide which combinations the host can turn into a request. Release matching uses the same facts. The [release match capability](media-types.md#adding-matching-path-or-routing-behavior) receives them, and the host's generic match treats a release as a confident match when its title equals, ignoring case, the space-joined text of a group in which every fact has text and no facets.

## Registering the provider

The [TV fact provider](../../../src/Magpie.Extensions.Tv/ContentFactProvider.cs) is a complete example. Register yours in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point):

```csharp
services.AddSingleton<IContentFactProvider, EpisodeFactProvider>();
```

`EpisodeFactProvider` is your own class, and the contract is in `Magpie.Sdk.Content`. A stateless provider can be a [singleton](../using-built-in-services.md#service-lifetime-and-ownership), and one that uses scoped [built-in services](../using-built-in-services.md) must be scoped. A provider exception propagates and can fail release evaluation, so return no groups when the facts are missing and throw only when the source is unavailable. Never fill gaps with invented identity data.

See [IContentFactProvider](../../../src/Magpie.Sdk/Content/IContentFactProvider.cs), [ContentFact](../../../src/Magpie.Models/Releases/ContentFact.cs), and [ReleaseEvaluationService](../../../src/Magpie.Core/Releases/ReleaseEvaluationService.cs).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
