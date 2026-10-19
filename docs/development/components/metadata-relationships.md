# Metadata relationships

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

A [content metadata provider](metadata-providers.md) can also name the [subjects](subject-types.md) related to an item: a book's publisher, a film's director. The host resolves those subject identities and attaches the relationships to the provider's contribution. You add this capability when your metadata source knows about related people or organizations.

Implement `IContentHierarchyMetadataRelationshipProvider` on the same class as `IContentHierarchyMetadataProvider`. It extends the content provider contract, and you register the class through the base contract in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point):

```csharp
services.AddScoped<IContentHierarchyMetadataProvider, CatalogMetadataProvider>();
```

After a provider returns a contribution, the host checks that same object for the relationship capability. Registering the optional interface separately does not attach it to another content provider.

## Returning a relationship

`GetRelationshipsAsync` receives the item, the newly fetched contribution, existing metadata, and ancestor metadata. Use the contribution's confirmed [source identity](metadata-providers.md#following-a-content-refresh) to look up relationships, not an ambiguous display name.

This builds one entry for the returned list, assuming the source has confirmed publisher record `publisher-42`. It needs `Magpie.Models` and `Magpie.Models.Data`.

```csharp
var publisher = new SubjectRelationshipDefinition
{
    RelationshipKind = "publisher",
    RelationshipOrdinal = 0,
    SubjectKind = "example-publisher",
    SubjectName = "Example Press",
    SubjectExternalIds = new Dictionary<string, string>
    {
        ["example-catalog"] = "publisher-42"
    },
    InitialSubjectContribution = new MetadataContribution
    {
        ProviderType = "example-catalog",
        Title = "Example Press",
        ExternalIds =
        {
            new() { Source = "example-catalog", Value = "publisher-42" }
        }
    }
};
```

The [TMDB content provider](../../../src/Magpie.Extensions.Tmdb/ContentMetadataProvider.cs) shows both the metadata and relationship methods in full.

The `example-publisher` kind and `publisher` role are declared in the [subject type example](subject-types.md#declaring-a-publisher-kind), and the identifiers must agree. `SubjectName` and `SubjectExternalIds` describe the incoming relationship and are not copied into the new subject's metadata. The host stores `InitialSubjectContribution` instead, so set its `Title` and `ExternalIds` to give the new subject a name and a stored external identity. It can carry any other known metadata too, which saves waiting for a subject refresh.

When the seed's `ProviderType` is stable and includes the matching source ID, the host derives a deterministic subject ID from the kind, provider type, and external ID. Without a matching provider identity it creates a random ID. Supply the identity in the first result, because later refreshes don't change the ID. When `SubjectExternalIds` has a non-blank source and value, the host also queues a metadata refresh for the new subject.

Use `RelationshipQualifier` when a role needs more detail, and `RelationshipOrdinal` for display order, such as ordered credits.

## Ownership and failure

Relationships belong to the contribution that produced them, and several providers can each contribute their own. Keep provider identity and subject external IDs consistent across refreshes.

The host skips definitions with a blank relationship kind, subject kind, or subject name, and logs a warning for an unknown subject kind. Conflicting incoming IDs or ambiguous stored matches leave the association unresolved with a warning. Relationship expansion runs only when the provider supplies a contribution.

Pass cancellation through any additional remote requests. If the relationship source is unavailable, throw instead of returning an empty list. In the [content refresh job](../../administration/jobs-and-automation.md#jobs-and-executions) the host logs the exception as an error and saves that provider's contribution without related subjects, and the content title still updates.

The provider only returns definitions. It doesn't write subject records, because the host handles identity resolution and persistence. It is [scoped](../using-built-in-services.md#service-lifetime-and-ownership) and can use [built-in services](../using-built-in-services.md).

See the [relationship contract](../../../src/Magpie.Sdk/Content/IContentHierarchyMetadataRelationshipProvider.cs), [request](../../../src/Magpie.Models/MetadataRelationshipRequest.cs), and [relationship fields](../../../src/Magpie.Models/SubjectRelationshipDefinition.cs).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
