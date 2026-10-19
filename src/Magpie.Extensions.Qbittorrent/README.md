![Magpie](logo.png)

# qBittorrent extension for Magpie

[Magpie](https://github.com/magpie-media-platform/magpie) is a self-hosted media manager for any media type, including movies, TV shows, music, and books, with the ability to add more through extensions.

The qBittorrent extension integrates Magpie with qBittorrent, so Magpie can use it as a [download client](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/storage-and-services.md#service-roles).
Magpie sends torrent [releases](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/search-and-download.md) to a running qBittorrent and follows their progress.
Magpie then [imports the completed files](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/search-and-download.md#following-completion) into the library.

## Adding a connection

With [Admin access](https://github.com/magpie-media-platform/magpie/blob/main/docs/reference/roles.md), open **Admin > Download Clients**, select qBittorrent, and set **Host** to the full WebUI address, including scheme and port.
Add the **Username** and **Password** if qBittorrent requires them, then test and save.

| Setting | Default and effect |
| --- | --- |
| Category | `magpie`. Set this category's save path in qBittorrent to the download directory. |
| Add as paused | Off. When on, each new torrent waits for a manual **Resume**. |
| Honor client hold policy | On. Cleanup waits until qBittorrent releases the imported torrents, including any seeding holds. |

In containers, `localhost` is the Magpie container. Use a name that resolves on a shared container network, such as `http://qbittorrent:8080`.

## Preparing the completed-file path

Magpie has to read the files qBittorrent reports. If qBittorrent reports `/downloads` but Magpie sees them at `/media/downloads`, add a [remote path mapping](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/storage-and-services.md#remote-paths). A mapping does not mount storage or grant access.

## Verifying a download

Grab one release and follow it on the **Active** tab of **Downloads**, selecting **Resume** if **Add as paused** is on.
When the transfer completes, follow it on the **History** tab through import and find the file in the title's **Files** view.

Torrent imports copy the files and leave the source for qBittorrent, so both copies need space. If the client finishes but import fails, check the reported path, the mapping, and permissions.
For **Forget** versus **Remove**, see [download actions](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/search-and-download.md#controlling-or-removing-a-download).

---

[First-party extensions](https://github.com/magpie-media-platform/magpie/blob/main/docs/extensions.md) | [Documentation](https://github.com/magpie-media-platform/magpie/blob/main/docs/README.md)
