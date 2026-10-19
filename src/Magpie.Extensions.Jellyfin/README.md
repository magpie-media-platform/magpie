![Magpie](logo.png)

# Jellyfin extension for Magpie

[Magpie](https://github.com/magpie-media-platform/magpie) is a self-hosted media manager for any media type, including movies, TV shows, music, and books, with the ability to add more through extensions.

The Jellyfin extension integrates Magpie with Jellyfin, so Magpie can use it as a [media server](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/storage-and-services.md#service-roles).
Magpie asks Jellyfin to refresh its library when media files change, for example after an [import](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/search-and-download.md#following-completion), and can link to the matching Jellyfin items.

## Adding a connection

With [Admin access](https://github.com/magpie-media-platform/magpie/blob/main/docs/reference/roles.md), open **Admin > Media Servers**, select Jellyfin, and enter the full **Host URL** and a Jellyfin **API Key**.
Run the connection test, then under **Library mappings** map each Magpie [root folder](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/storage-and-services.md#adding-a-root-folder) and [content type](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/collection.md#content-types-and-hierarchy) to a Jellyfin library, and save.

For example, mapping the root `/media/movies` and the movie content type to the Jellyfin Movies library means Magpie refreshes only that library for those files. If no mapping matches, Magpie refreshes all Jellyfin libraries.

Jellyfin needs access to the same media files. The connection test checks the API only.

## Verifying a refresh

With the default [automation](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/jobs-and-automation.md#jobs-and-executions), a change to an item's media files queues a **Media server refresh** job about 30 seconds later. You can also run the job manually.

The job can complete even if Jellyfin rejects the request or cannot be reached, so check the Magpie [host logs](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/observability.md#reading-host-logs) for warnings that name the Jellyfin connection.

If the item does not show, check the selected library and Jellyfin's access to the imported path. If the link is missing, the [identifiers](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/metadata.md#understanding-values-with-several-entries) may differ.
See also [troubleshooting](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/troubleshooting.md#media-server-does-not-show-an-imported-file).

---

[First-party extensions](https://github.com/magpie-media-platform/magpie/blob/main/docs/extensions.md) | [Documentation](https://github.com/magpie-media-platform/magpie/blob/main/docs/README.md)
