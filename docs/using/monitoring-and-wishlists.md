# Monitoring and wishlists

[Documentation](../README.md) / [Everyday use](README.md)

Monitoring tells Magpie to [acquire](search-and-download.md#selecting-a-release) releases for items already in the [collection](collection.md). A wishlist searches [metadata providers](../administration/metadata.md) and adds matching titles to the collection, such as every movie by a director, with a monitoring mode you choose.

## Setting monitoring

Changing the monitoring of an existing item needs [Manager access](../reference/roles.md) or higher.

| Mode | Effect |
| --- | --- |
| Unmonitored | Keeps the item without automatic acquisition. |
| Monitored | Acquires the best acceptable release when the item needs media or an upgrade. |
| Shotgun | Acquires several acceptable releases at once, up to the [configured limit](../administration/configuration.md#setting-the-system-monitoring-default). This can use a lot of bandwidth and storage. |

A [child](collection.md#content-types-and-hierarchy) can [inherit monitoring](../administration/configuration.md#understanding-collection-defaults) from its parent. A child with its own setting keeps it.

## Understanding wanted items and upgrades

The **Wanted** view lists monitored items that have no media or need an upgrade. Open a container such as a show to see its children.

Fulfillment is the state of an item's media.

| State | Meaning |
| --- | --- |
| Missing | The item has no media files. |
| Upgradable | The item has media files and accepts upgrades. |
| Fulfilled | The item has media files and needs no more. |
| FulfilledExternally | An external source fulfills the item, so it needs no acquisition. |
| Partial | A container with some, but not all, of its descendant leaves covered. |

A title with a file can still need an upgrade, because its quality tier list sets the target. The [query reference](../reference/magpieql.md#content-fields) uses the same state names.

## Creating and verifying a wishlist

Creating a wishlist needs Requester access or higher. Owners manage their own wishlists, and administrators manage the rest.

In **Wishlists**, give the wishlist a name and a [content type](collection.md#content-types-and-hierarchy), then enter criteria. The [query guide](../power-user/filters-and-magpieql.md#collection-filtering-and-external-discovery) explains which criteria work. A refresh adds every new match, so use a narrow query. **Preview** shows sample titles and provider warnings without adding anything. You also choose the monitoring, [root folder](../administration/storage-and-services.md#adding-a-root-folder), quality tier list, and [tags](collection.md#subjects-and-tags) for new titles. **Unmonitored** adds titles without acquiring them.

A new enabled wishlist refreshes when it is created, and a daily scan at 06:00 UTC refreshes it later. After the first refresh, check the new titles for correct identity, monitoring, tags, and destination.

Manually refreshing a wishlist needs Admin access. If automatic refresh does not run, ask an administrator to check the [wishlist jobs and triggers](../administration/jobs-and-automation.md#jobs-and-executions).

A valid query can include criteria a provider does not support. Provider warnings report these limits.

## Changing or stopping a wishlist

A wishlist's settings apply only to new titles. Change existing items with their own controls.

Disabling a wishlist stops automatic discovery. Disabling or deleting a wishlist does not remove the titles it added. To remove them, see [Removing a title](collection.md#removing-a-title). When you delete a wishlist, Magpie may offer to delete its saved filter, which anything else using it also loses.

## Keeping a removed title out of the collection

A wishlist does not add a removed title again, but a different wishlist can.

To stop all additions, disable each matching wishlist before removing the title and wait for any running [refresh](../administration/jobs-and-automation.md#jobs-and-executions) to finish. If the title can stay, set it to **Unmonitored**.

---

[Everyday use](README.md) | [Documentation](../README.md)
