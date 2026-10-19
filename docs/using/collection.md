# Managing a collection

[Documentation](../README.md) / [Everyday use](README.md)

The collection is the set of titles Magpie knows about. A title has [metadata](../power-user/metadata.md) and lists its media [files](files.md). Adding a title creates the record only. Files arrive when a download is imported or when you run a [Library import](files.md#importing-existing-files).

A typical flow is to add a movie, search for [releases](search-and-download.md) of it, and let a [download client](../administration/storage-and-services.md#service-roles) fetch the one you pick. Magpie then [imports](search-and-download.md#following-completion) the files.

## Adding a title

**Add content** searches external metadata sources. It needs [Requester access](../reference/roles.md) or higher, and an administrator must have configured a [metadata provider](../administration/metadata.md) and a [root folder](../administration/storage-and-services.md#adding-a-root-folder) for the [content type](#content-types-and-hierarchy).

A result's year, description, and [provider identifiers](../power-user/metadata.md#understanding-values-with-several-entries) tell similar names apart. For music and books, see [album identity](../../src/Magpie.Extensions.MusicBrainz/README.md#matching-an-album-before-importing-files) and [book identity](../../src/Magpie.Extensions.Books/README.md#checking-identity-before-import).

Magpie then queues a [metadata refresh](../power-user/metadata.md#verifying-a-metadata-refresh), and child items appear when it finishes. A new title may be monitored right away through an [inherited default](../administration/configuration.md#understanding-collection-defaults). A Manager or higher can change its [monitoring](monitoring-and-wishlists.md#setting-monitoring), [quality](../administration/storage-and-services.md#quality-choices), and root folder.

A file that is already in a library folder is added through [Library import](files.md#importing-existing-files) instead.

## Finding content in the collection

The library searches titles already in the collection. Pick a content type and narrow the library with [filters](../power-user/filters-and-magpieql.md), which you can save. Opening a title shows its files, releases, related [subjects](#subjects-and-tags), and activity.

### Content types and hierarchy

A content type is a kind of media, such as movies or TV. Movies have one level, TV has shows, seasons, and episodes, and music has artists, albums, and tracks.

A parent groups its children. A leaf, such as a movie or an episode, is what Magpie acquires. A parent can be [partly fulfilled](monitoring-and-wishlists.md#understanding-wanted-items-and-upgrades) when only some of its children have files.

### Subjects and tags

Subjects are people and groups that [extensions](../administration/extensions.md) supply, such as an actor linked to a movie. A subject links to its other titles.

Tags group items for your own purposes. They do not affect monitoring or quality.

### Calendar

The calendar shows content by its stored dates. If a date is missing or wrong, [correct the metadata](../power-user/metadata.md#correcting-one-field).

## Removing a title

Removing needs Manager access or higher. Select **Remove from library** on the title. This also removes everything under it, so removing a show removes its seasons and episodes.

By default the files stay on disk. **Also delete files from disk** deletes the associated media files too, and the dialog lists them.

A [wishlist](monitoring-and-wishlists.md#keeping-a-removed-title-out-of-the-collection) can add the title again. To bring a title back from files you kept, use [Library import](files.md#importing-existing-files) with **Unmonitored**. To keep the title and drop one file, use [Unlink](files.md#unlinking-or-deleting-a-file).

---

[Everyday use](README.md) | [Documentation](../README.md)
