# Subject type definitions

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

Subjects are the people, organizations, and other entities related to [content](../../using/collection.md). A book is content, a publisher is a subject, and a relationship says how the subject takes part in the content. You declare a subject kind when Magpie has none that fits the entities your extension deals with.

`ISubjectKindDefinitionProvider` declares a [subject kind](#keeping-kind-role-and-identity-distinct)'s labels, fields, and allowed relationship roles. It doesn't create subjects or fetch their metadata. [Metadata providers](metadata-providers.md) supply the values, and [relationship providers](metadata-relationships.md) connect subjects to content.

## Declaring a publisher kind

This complete component declares an extension-specific publisher kind. Add it to an extension project with implicit usings.

```csharp
using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Content;

internal sealed class PublisherDefinitionProvider : ISubjectKindDefinitionProvider
{
    /// <inheritdoc />
    public IEnumerable<SubjectKindDefinition> GetSubjectKindDefinitions()
    {
        return
        [
            new()
            {
                Slug = "example-publisher",
                DisplayName = "Publisher",
                PluralDisplayName = "Publishers",
                SupportedMetadataFields = MetadataField.Title | MetadataField.ExternalIds,
                CustomMetadataFields = [],
                RelationshipRoles =
                [
                    new()
                    {
                        Slug = "publisher",
                        DisplayName = "Publisher"
                    }
                ]
            }
        ];
    }
}
```

Register it in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point):

```csharp
services.AddSingleton<ISubjectKindDefinitionProvider, PublisherDefinitionProvider>();
```

The host enumerates registered providers to build the subject-kind registry, and one provider can return several definitions. The method is synchronous with no cancellation token, so keep remote requests out of it and keep the declaration stable while the host runs. The [singleton](../using-built-in-services.md#service-lifetime-and-ownership) provider must not hold scoped services.

Each slug has one definition. If several providers declare the same slug, the registry keeps the [last registration](README.md#registration-order) and logs a warning. If a provider throws, the registry logs a warning and ignores it.

## Keeping kind, role, and identity distinct

In the example, `example-publisher` is the subject kind and `publisher` is a relationship role. A relationship provider returns both. The subject's display name, such as **Example Press**, is only a label, and identity comes from an [external identifier](metadata-providers.md#following-a-content-refresh). Use the same kind slug in the definition, in a metadata provider's supported kinds, and in returned relationships, or the components can't recognize each other's kind.

External identifiers give a subject a source-specific identity across refreshes. When none matches, the host can compare the scalar values that `FallbackIdentityFields` selects from the subjects' [metadata contributions](../../power-user/metadata.md#inspecting-a-conflicting-value). Both subjects need a contribution, every selected value must be present and equal, and the selection must include at least one field besides `Title`, `OriginalTitle`, or `SortTitle`. String comparison ignores case and surrounding spaces. This fallback only compares subjects resolved during the same content refresh, such as one person returned by two providers. It never matches a subject stored by an earlier refresh, which is what external identifiers are for.

The supported fallback fields are `Title`, `OriginalTitle`, `SortTitle`, `Language`, `Overview`, `PublishDate`, `Duration`, `SequenceNumber`, `ProductionStatus`, `Established`, `Dissolved`, `BirthDate`, and `DeathDate`. If the selection includes anything else, such as a collection of external IDs, or is name-only, fallback matching is off. For a person, `MetadataField.Title | MetadataField.BirthDate` works when both contributions have those values and the source data really tells people apart. The publisher sample leaves fallback fields unset and relies on external identity. A relationship's `InitialSubjectContribution` supplies the values for an incoming subject, as described in [relationship persistence](metadata-relationships.md#returning-a-relationship).

The [Books publisher definition](../../../src/Magpie.Extensions.Books/Subjects/PublisherSubjectKindDefinitionProvider.cs) is the first-party kind. If an existing kind models the same entity, reuse it.

See the [provider contract](../../../src/Magpie.Sdk/Content/ISubjectKindDefinitionProvider.cs), [definition fields](../../../src/Magpie.Models/SubjectKindDefinition.cs), and [registry behavior](../../../src/Magpie.Core/Subjects/SubjectKindRegistry.cs).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
