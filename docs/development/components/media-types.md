# Content type definitions

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

A content type defines the [collection structure](../../using/collection.md#content-types-and-hierarchy) that the rest of Magpie builds on. A television type, for example, has show, season, and episode levels. A level can accept files, be something you acquire releases for, or just group its children. You write a content type to teach Magpie a new kind of media.

`IContentTypeDefinitionProvider` declares the type and parses file paths into [search signals](#defining-one-level-and-a-file-convention). It does not describe items from an external catalog. [Metadata providers](metadata-providers.md) do that, and [indexers](indexers.md) and [download clients](download-clients.md) supply their own behavior.

## Defining one level and a file convention

This complete component defines a lecture type. Its parser recognizes `Lecture-<title>.mp4`, such as `Lecture-Orbital mechanics.mp4`, and returns null for other names. Add it to the project from [Creating an extension](../extensions.md).

```csharp
using Magpie.Models;
using Magpie.Models.Signals;
using Magpie.Sdk.Content;

internal sealed class LectureDefinitionProvider : IContentTypeDefinitionProvider
{
    /// <inheritdoc />
    public ContentTypeDefinition Definition => new()
    {
        Slug = "lecture",
        DisplayName = "Lecture",
        PluralDisplayName = "Lectures",
        LevelDefinitions =
        [
            new()
            {
                Slug = "lecture",
                DisplayName = "Lecture",
                CustomMetadataFields = [],
                AcceptedFileTypes = [".mp4"],
                IsAcquirable = true,
                Subfolder = string.Empty
            }
        ]
    };

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Parse(
        string filePath,
        out string? levelSlug)
    {
        levelSlug = null;
        var name = Path.GetFileNameWithoutExtension(filePath);
        const string prefix = "Lecture-";
        if (!string.Equals(Path.GetExtension(filePath), ".mp4", StringComparison.OrdinalIgnoreCase) ||
            !name.StartsWith(prefix, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(name[prefix.Length..]))
        {
            return null;
        }

        levelSlug = "lecture";
        return new SignalBagBuilder()
            .AddValue(ParsedFileSignalKeys.Title, name[prefix.Length..])
            .AddValue(ParsedFileSignalKeys.TitleToken, name[prefix.Length..])
            .Build();
    }
}
```

Register it in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point):

```csharp
services.AddSingleton<IContentTypeDefinitionProvider, LectureDefinitionProvider>();
```

During a [library import](../../using/files.md#importing-existing-files), the match job calls `Parse` for each file. It searches metadata with the `rootTitle` signal, or else `titleToken`, or else `title`, and keeps only results at the returned level. A null result leaves the file unmatched with the note `The filename contains no title.`

`Definition` and `Parse` are synchronous and called often, so keep network work out of them. The registry is long-lived, so the [singleton](../using-built-in-services.md#service-lifetime-and-ownership) provider must handle concurrent calls and must not capture scoped services.

## Modeling a hierarchy and its paths

A slug is a text identifier for a type or a level. Use stable lowercase slugs, set a child's `ParentSlug` to its parent level, give the root no parent, define each level once, and order the list from root to leaf. Magpie stores each item of the hierarchy as a content hierarchy item, and metadata operations take its `ContentHierarchyItem.Id`. See [Reading collection data and metadata](../using-built-in-services.md#reading-collection-data-and-metadata) for the identifiers.

Other fields on a level decide how it behaves:

| Field | Effect |
| --- | --- |
| `IsAcquirable` | Allows items at that level to be release-acquisition targets. |
| `AcceptedFileTypes` | Lists the extensions, such as `.mp4`, or MIME patterns, such as `video/*`, of the primary files at this level. A library scan collects only matching files, and a type with no accepted file types is not scanned. |
| `AcceptedSidecarTypes` | Identifies associated files such as subtitles. |
| `SupportedMetadataFields`, `CustomMetadataFields` | Describe the metadata available for that level. |
| `Subfolder = null` | The level contributes no path segment. |
| `Subfolder = ""` | The level uses a generated item path. |
| A named `Subfolder` | The level supplies that root subfolder contribution. |

The lecture type has one root level that owns files and is acquired. A series-like type needs separate grouping and file-owning levels, because if every level has the same flags the wrong items can become download targets. The [type model](../../../src/Magpie.Models/ContentTypeDefinition.cs) and [level model](../../../src/Magpie.Models/ContentHierarchyLevelDefinition.cs) also define icons, categories, ordinal kinds, and ordered standard fact fields.

Each type slug has one definition. If several providers declare the same slug, the registry keeps the [last registration](README.md#registration-order) and logs a warning. It logs a warning and ignores a provider whose definition can't be read, and logs an error and ignores a definition whose type or level slug isn't lowercase.

## Adding matching, path, or routing behavior

Implement optional capabilities on the same provider and register it through `IContentTypeDefinitionProvider`.

| Capability | Host call and result |
| --- | --- |
| `IReleaseMatchContentTypeDefinitionProvider` | Evaluates a release against the target and [search facts](content-facts.md). Return the strength of the match, or an indeterminate result when the evidence is insufficient. |
| `IPathResolutionContentTypeDefinitionProvider` | Builds a path or directory-ownership plan from the ancestor chain. A null result selects default path behavior. |
| `IFileRoutingContentTypeDefinitionProvider` | Routes parsed files among direct children of a confirmed parent. Return child IDs in file order, or an empty list if unresolved. |

Magpie also computes a generic match for each release. If either result is `Confident` or `Identity`, the higher one applies. Otherwise a provider `NoMatch` rejects the release, and a provider `Indeterminate` keeps the generic result. If `IsMatch` throws, Magpie logs it and uses the generic result.

Parsing, release matching, and child routing answer different questions. An episode number in a filename does not identify the show, and child routing runs with a confirmed parent, so it only chooses among that parent's children.

For full implementations, see the [Movies provider](../../../src/Magpie.Extensions.Movies/MoviesContentTypeDefinitionProvider.cs) and the [TV extension](../../../src/Magpie.Extensions.Tv). The [path contract](../../../src/Magpie.Sdk/Content/IPathResolutionContentTypeDefinitionProvider.cs) defines ownership plans and its default methods.

Contracts: [provider](../../../src/Magpie.Sdk/Content/IContentTypeDefinitionProvider.cs), [type](../../../src/Magpie.Models/ContentTypeDefinition.cs), [level](../../../src/Magpie.Models/ContentHierarchyLevelDefinition.cs), and [registry](../../../src/Magpie.Core/Content/ContentTypeRegistry.cs).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
