# Release comparers

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

A release comparer adds a preference among [releases](../../using/search-and-download.md) that Magpie's primary ranking leaves tied, such as a smaller file or more available peers. It only orders releases within one primary group, and never moves a release across a match or quality boundary. Ties are more common than they sound: when a [quality cutoff](../../administration/storage-and-services.md#quality-choices) treats several qualities as sufficient, those releases share an effective rank, and a comparer can choose among them.

## A size preference

This complete component prefers smaller releases, using `Release.Size` in bytes. Add it to the [extension tutorial's project](../extensions.md), which supplies implicit usings.

```csharp
using Magpie.Models.Data;
using Magpie.Sdk.Releases;

internal sealed class SmallerReleaseComparer : IReleaseComparer
{
    /// <inheritdoc />
    public bool CanCompare(Release release) => release.Size is not null;

    /// <inheritdoc />
    public int Compare(Release left, Release right) =>
        Nullable.Compare(left.Size, right.Size);
}
```

Register it in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point):

```csharp
services.AddSingleton<IReleaseComparer, SmallerReleaseComparer>();
```

A negative result puts the left release first, and zero leaves the tie for a later comparer.

## How comparers compose

Search ranking first groups releases by match result and effective quality rank. Automatic acquisition first picks acceptable releases, then groups them by previous fault status and effective quality rank, so a comparer can't promote a faulted release above a non-faulted one. Both apply comparers in registration order, and each orders the ties earlier comparers left. After the last comparer, search ranking puts the newest `PublishDate` first, and automatic acquisition orders what remains by matched quality tier, then release ID.

`CanCompare` must return true for every release in a tied group before the host applies the comparer to it. With sizes of 400 MB and 800 MB the sample prefers 400 MB, but if a third tied candidate has no size, the comparer skips the group instead of ranking only the two known sizes. This keeps the ordering consistent. For provider-specific data, `CanCompare` should reject unsupported or invalid properties before `Compare` uses them. The [Cardigann comparer](../../../src/Magpie.Extensions.Cardigann/CardigannReleaseComparer.cs) shows seed and peer preferences. Search execution also uses comparers to pick the stronger of two evaluated releases, and `CanCompare` must be true for both.

Comparisons must be deterministic and transitive: if A ranks before B and B before C, C can't rank before A. The contract is synchronous with no cancellation token, so keep file and network calls out of it. Magpie can call a [singleton](../using-built-in-services.md#service-lifetime-and-ownership) comparer concurrently.

See the [contract](../../../src/Magpie.Sdk/Releases/IReleaseComparer.cs), [interactive ranking](../../../src/Magpie.Core/Releases/ReleaseSearchService.cs), [search execution](../../../src/Magpie.Core/Releases/ReleaseSearchQueryExecutor.cs), and [automatic acquisition](../../../src/Magpie.Core/Releases/ReleaseService.cs).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
