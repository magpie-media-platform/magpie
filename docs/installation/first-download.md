# Downloading the first movie

[Documentation](../README.md) / [Installation](README.md)

This walks one movie through the whole pipeline on a new installation: TMDb supplies the title, an indexer finds releases, qBittorrent downloads, and Magpie imports the file into your library. See [Searching and download](../using/search-and-download.md) for everyday use.

## Preparing the requirements

The first download requires a finished [initial setup](../quick-start.md#completing-setup), the [administrator account](../reference/roles.md), and:

- A [TMDb API key](../../src/Magpie.Extensions.Tmdb/README.md#configuring).
- Access to an indexer supported by [Cardigann](../../src/Magpie.Extensions.Cardigann/README.md), with its credentials.
- A running qBittorrent WebUI, its address and credentials, and storage for completed downloads and imported files.

The [Docker Standard image](docker.md#selecting-an-image) already includes the needed [extensions](../administration/extensions.md): Movies, TMDb, Cardigann, and qBittorrent. Otherwise they must be [installed](../administration/install-extensions.md). [MediaInfo (FFprobe)](../../src/Magpie.Extensions.MediaInfo.FFProbe/README.md) also needs `ffprobe`, which the Docker images include.

## Connecting storage

Magpie and qBittorrent must see the same files. For [Windows](windows.md#giving-the-service-access-to-media), [standalone](standalone.md#installing-extensions-and-completing-setup), [Kubernetes](kubernetes.md#preparing-the-namespace-and-storage), or [Helm](helm.md#media-root-folders-and-download-paths), follow that page's storage section and use paths Magpie can see.

On a Linux Docker host, this layout works. Create both directories, give qBittorrent write access to `downloads`, and give Magpie read access to `downloads` and write access to `movies`.

| Purpose | Host path | Path in Magpie and qBittorrent |
| --- | --- | --- |
| Completed downloads | `/srv/media/downloads` | `/media/downloads` |
| Imported movies | `/srv/media/movies` | `/media/movies` |

Mount `/srv/media` at `/media` in both containers. To add the mount to the quick-start Magpie container, replace it. The `magpie-data` volume keeps your setup. To run Magpie as the owner of `/srv/media`, add `-e PUID=<uid> -e PGID=<gid>`.

```sh
docker stop magpie
docker rm magpie
docker run -d --name magpie --restart unless-stopped \
  -p 127.0.0.1:8080:8080 \
  -v magpie-data:/app/data \
  --mount type=bind,source=/srv/media,target=/media \
  ghcr.io/magpie-media-platform/magpie:latest
```

If qBittorrent runs in another container, put both on one Docker network, for example `docker network connect media-services magpie`. See [Docker networks](docker.md#connecting-to-magpie-and-other-services).

## Configuring TMDb

1. Open **Admin > TMDb**.
2. Enter the **API key**.
3. Select **Save Settings**.

## Connecting an indexer

1. Open **Admin > Indexers > Available**.
2. Select **Configure** on the indexer and enter its settings.
3. Select **Test**, and correct any error.
4. Select **Add**. The indexer should appear under **Configured** and be enabled.

If **Available** is empty, synchronize the feed as described in the [Cardigann instructions](../../src/Magpie.Extensions.Cardigann/README.md#configuring-definitions-and-indexers).

## Connecting qBittorrent

In the qBittorrent WebUI, open the context menu in **Categories** and select **Add category...**. Set **Category** to `magpie` and **Save path** to the download directory, `/media/downloads` in the Docker example. If the category exists, use **Edit category...** to check the path. If qBittorrent reports a different path, add a [remote path mapping](../administration/storage-and-services.md#remote-paths).

Then in Magpie:

1. Open **Admin > Download Clients** and select **Add Client**.
2. Set **Integration** to qBittorrent and enter a **Name**.
3. Enter the full WebUI URL in **Host**, then the username and password. In Docker, `localhost` is Magpie itself, so use the container name, such as `http://qbittorrent:8080`, or the other machine's address.
4. Keep **Category** set to `magpie`, leave **Add as paused** off, and keep the other defaults.
5. Save and test the connection.

## Adding the movie folder

A [root folder](../administration/storage-and-services.md#adding-a-root-folder) is where imported files go.

1. Open **Admin > Root Folders** and select **Add Folder**.
2. Set **Name** to `Movies` and **Path** to `/media/movies`, or to your prepared path.
3. Select the movie [**Content Type**](../using/collection.md#content-types-and-hierarchy).
4. Select **Video - HD** under [**Default Quality Tiers**](../administration/storage-and-services.md#quality-choices).
5. Under [**Default monitoring**](../using/monitoring-and-wishlists.md#setting-monitoring), turn off **Use system default** and select **Unmonitored**.
6. Save the folder.

**Unmonitored** leaves release selection to the manual search below.

## Downloading and checking the file

Pick a movie your indexer offers and use it throughout.

1. Open **Add content** and select **Live filter**. Select the movie content type, enter the title, and select **Search**.
2. Add the matching movie, checking the year, with the `Movies` root folder.
3. Open its details and confirm **Root Folder** shows `Movies` and **Quality** has a selection.
4. Select [**Interactive Search**](../using/search-and-download.md#selecting-a-release), review the releases, and select **Grab** on a suitable one.
5. Follow the transfer under **Downloads > Active**. `Grabbed` means qBittorrent accepted the release.
6. Open **History** and follow the download through `Downloaded`, `Importing`, and `Imported`.
7. In the **Files** view of the movie, confirm the imported file is listed.

| Result | What to check |
| --- | --- |
| No title in Add content | The TMDb key and the title filter. This search uses metadata only, not releases. |
| No suitable release | The indexer result and rejection details. See [Searching and download](../using/search-and-download.md#interpreting-an-unsuccessful-search). |
| Grab fails or the transfer stops | The download details, the client connection, and the qBittorrent status. |
| Download completes but the file is not in Files | The path the client reports against the path in Magpie. See [import troubleshooting](../administration/troubleshooting.md#download-completes-but-import-does-not). |

Continue with [Everyday use](../using/README.md).

---

[Installation](README.md) | [Documentation](../README.md)
