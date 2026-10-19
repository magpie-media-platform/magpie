# Configuration

[Documentation](../README.md) / [Administration](README.md)

Host settings are read at startup and select the database, the persistent directories, the network listener, and the [cluster mode](clustering.md). Instance settings are saved in the database and control collection behavior, service connections, and appearance.

## Understanding collection defaults

A movie is one collection item, while a television show contains seasons and episodes. The [content extension](extensions.md) defines this structure. A release is a candidate download offered by an [indexer](storage-and-services.md#service-roles), a download is work sent to a [download client](storage-and-services.md#service-roles), and a media file is a physical file that belongs to an item.

Root folders connect the collection to storage. Each has a content type, a path, [monitoring defaults](../using/monitoring-and-wishlists.md#setting-monitoring), and [quality defaults](storage-and-services.md#quality-choices) for its content levels. A new movie in a `Movies` root folder with **Unmonitored** and **Video - HD** inherits both.

Magpie uses the first explicit choice on the item or its ancestors, then the root-folder default. For monitoring, the last fallback is the system default. Changing a root-folder choice does not move existing files. For that, see the [file operations](../using/files.md).

## Setting the system monitoring default

The system default applies when an item, its ancestors, and its root folder have no monitoring choice. It is on **Admin > Root Folders** in the **Monitoring** panel, and until you save a value it is **Monitored**. A change applies to every item that inherits it, so it can start or stop acquisition for many titles.

With **Shotgun** selected, **Max spread** limits how many releases Magpie grabs at once for one item, including active downloads. `0` means all acceptable releases.

## Changing host settings

Host values go in `config/appsettings.json` or in the process environment. The environment overrides the file, and a restart applies a change. This example sets a native Linux data directory:

```json
{
  "Magpie": {
    "Data": {
      "Path": "/var/lib/magpie"
    }
  }
}
```

The environment equivalent is `Magpie__Data__Path=/var/lib/magpie`, where double underscores separate levels. Changing the data path does not move the old database or keys, so stop Magpie and copy the state first. The [host configuration reference](../reference/configuration.md) lists every key.

## Changing instance settings

**Admin > Advanced** holds the instance settings:

- **Instance Name** is the displayed name of the installation.
- **Base URL** stores the external address of the instance. It does not create a listener, certificate, proxy, or DNS record. See [Accounts and access](access.md#publishing-a-network-address).
- **Job retry backoff** sets the default [retry policy](jobs-and-automation.md#configuring-retries) for failed jobs.
- [**Theme defaults**](#theme-defaults) sets the starting appearance.

Each panel has its own **Save Settings** button.

## Theme defaults

Under **Admin > Advanced > Theme defaults**, you choose the appearance mode and the light and dark themes. A browser's own choice overrides them. See [Appearance](../using/appearance.md).

A theme from an extension is available only while that extension is loaded. Otherwise the browser falls back to the built-in Tokyo Night dark theme.

---

[Administration](README.md) | [Documentation](../README.md)
