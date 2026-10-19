# Troubleshooting

[Documentation](../README.md) / [Administration](README.md)

Most problems come down to one stage of the path from a title to a file in your library failing: the host starting, an extension loading, a search, a download, an import, or a media server refresh. The error message and [execution ID](jobs-and-automation.md#jobs-and-executions) show which stage it was. [Doctor](doctor.md) scans for common setup problems.

| Symptom | Where to look |
| --- | --- |
| [Host does not become ready](#host-does-not-become-ready) | [Startup logs](observability.md#reading-host-logs), the [health body](observability.md#health-and-readiness), the database connection, and storage access |
| [Existing installation shows setup](#host-does-not-become-ready) | The effective database target and data path |
| [Extension feature is absent](#extension-or-provider-is-unavailable) | [Extension](extensions.md) version, enabled state, and startup error |
| [Title search returns no result](#search-returns-no-useful-result) | [Metadata provider](metadata.md) settings and coverage |
| [Release search returns no suitable result](#search-returns-no-useful-result) | [Indexer](storage-and-services.md#service-roles) results and [rejection reasons](../using/search-and-download.md#interpreting-an-unsuccessful-search) |
| [Download is not progressing](#download-is-not-progressing) | [Download client](storage-and-services.md#service-roles) status |
| [Completed download is not imported](#download-completes-but-import-does-not) | Source path, translation, permissions, destination, and the import message |
| [Work stays queued or cancellation does not finish](#work-remains-queued-or-cancellation-does-not-finish) | Execution state, owning node, and errors |
| [Imported file is absent from a media server](#media-server-does-not-show-an-imported-file) | The file, the server's library path, and the refresh result |

## Host does not become ready

Read the status in the body of `/api/system/health`. While startup continues, read the logs before restarting. If it failed, chase the first database or storage error: a refused connection points at the database address, listener, or network, an authentication error at the database account, a missing path at the service account, volume, or directory, and an extension error at the package the log names.

Environment variables override the JSON configuration, so check the environment the service, container, or pod actually has. [Databases](databases.md) lists the provider requirements.

If an existing installation shows initial setup, stop and do not create an administrator. An empty database usually means a missing volume or a changed working directory. Find the intended database target and data path first.

## Extension or provider is unavailable

On **Admin > Extensions**, an installed package loads only after a restart, and so do changes to enablement and order. If a package shows **Failed**, its reason and the startup logs say why, usually compatibility, a missing external program, or access to the package files. In a [cluster](clustering.md), check the node that ran the operation.

A loaded extension is not a configured integration. A download-client extension, for example, does not create a client, which needs its own connection. See [Extensions](extensions.md) and [Storage and services](storage-and-services.md).

## Search returns no useful result

For metadata, search for a known title with the right [content type](../using/collection.md#content-types-and-hierarchy) and check the provider's credentials, language or region, and coverage. See [Metadata](metadata.md).

For releases, look at the indexer's connection test and the actual result list. Title matching, [quality rules](storage-and-services.md#quality-choices), exclusions, or download-client compatibility can reject results, and the rejection reason says which. An Unhealthy indexer is skipped until a test succeeds (see [connection status](storage-and-services.md#connecting-a-service)).

For repeated automatic searches, look at [monitoring](../using/monitoring-and-wishlists.md#setting-monitoring), the quality list, existing files, and the search job history. The [minimum search age and coalescing window](../reference/configuration.md#background-work) can defer work. See [Search and downloads](../using/search-and-download.md) and [Jobs](jobs-and-automation.md).

## Download is not progressing

Open the download on the **Active** tab of **Downloads** and compare its status and error with the same transfer in the client.

| Evidence | Fix |
| --- | --- |
| The client shows a paused transfer | Resume it. If every new qBittorrent download pauses, check **Add as paused** in the connection settings. |
| Connection or authentication error in Magpie | Run the test under **Admin > Download Clients**, correct the address, credentials, or network, and test again. |
| The client reports a storage error | Fix the client's access to its download directory or free up space, then retry in the client. |
| The client is waiting for sources | Look at sources in the client. |
| The client has completed the transfer | See [import diagnosis](#download-completes-but-import-does-not). |

Wait until progress moves in both the client and Magpie before submitting another release. The [qBittorrent instructions](../../src/Magpie.Extensions.Qbittorrent/README.md) describe its connection settings.

## Download completes but import does not

The **Downloaded**, **Importing**, and **Imported** states appear on the **History** tab of **Downloads**, not **Active**. Magpie needs a readable source, a matching item, and a writable destination, and the message on the import execution says which is missing.

### Preserving a failed download

By default Magpie can remove failed downloads and their files from the client, even when a torrent is still seeding. For a **Failed** or **Faulted** download you want to investigate, edit the client under **Admin > Download Clients**, enable **Keep failed downloads**, and save. If cleanup is already running, stop Magpie on all nodes before copying the remaining files out of the client's cleanup path. If cleanup already deleted the source, only another backup or a new download brings the files back.

### Comparing the complete paths

Take the path the client reports and follow it to the file. With the storage example, the same file looks different at each layer:

| View | Path |
| --- | --- |
| Client at `http://qbittorrent:8080` | `/downloads/complete/Example.Movie.2024.1080p.WEB-DL/movie.mkv` |
| Docker host | `/srv/media/downloads/Example.Movie.2024.1080p.WEB-DL/movie.mkv` |
| Magpie container | `/media/downloads/Example.Movie.2024.1080p.WEB-DL/movie.mkv` |

A [remote path mapping](storage-and-services.md#remote-paths) with **Host** `qbittorrent`, **Remote Path** `/downloads/complete`, and **Local Path** `/media/downloads` translates the prefix, and the rest of the path has to point at the same file. Check that the file exists where the client says, that the storage is mounted into Magpie, and that the mapping's host matches the configured client. If the client's path already works in Magpie, no mapping is needed.

### Separating access checks

**Test Path** on a mapping only checks that the directory exists and is readable. Check the file and the destination as the Magpie process account.

| Result | Next step |
| --- | --- |
| Local directory absent | Fix the mount or local prefix. |
| Directory unreadable | Fix access for the service account or container. |
| Directory passes but source file absent | Compare the full translated path, including subdirectories and file name. |
| Source readable but destination fails | Check the title's effective [root folder](storage-and-services.md#adding-a-root-folder), write access, and free space. |
| Access works but import rejects the file | Read the matching, file-type, quality, or collision message. |

[Storage and services](storage-and-services.md#giving-both-processes-the-required-access) covers copy versus move permissions and the space an import needs.

### Recovering after the correction

| State | Recovery |
| --- | --- |
| Downloaded or Importing | Magpie can still import or recover these. Look for a queued attempt before starting another operation. |
| Failed or Faulted with an import failure | A path correction does not reset this final state. If the completed source still exists, recover the files by hand as below. |
| Imported | Check the associated files and any recovery work. Do not request another download because a later operation failed. |

The download retry API requests another acquisition. It does not import existing completed files again.

To recover files by hand, first check the title's **Files** view and the destination for anything the earlier import already did. Set the title to **Unmonitored** while you work so monitoring does not start another acquisition, and note the previous setting. Copy only the missing completed files into the library root, leaving torrent source files in place for the client and checking before overwriting anything. Then use [Library import](../using/files.md#importing-existing-files), or [Import file](../using/files.md#importing-one-file) for a single file, to match them to the collection items. Verify the paths in **Files** and restore the monitoring choice.

This does not change the old failed record to **Imported**, so keep its error evidence until you have verified the result.

## Work remains queued or cancellation does not finish

Open the execution and look at its trigger, progress, attempt, node, and related work, and whether another attempt is already queued. Queued work can be waiting for a free worker, a delayed retry, or a node that can start jobs. In a cluster, [Clustering](clustering.md#verifying-a-node-before-normal-use) lists the node checks. After a node failure, work waits for recovery of stale work.

A cancelled job stops only when its worker receives the request. Check any external operation before submitting a replacement. In a cluster, [Clustering](clustering.md#investigating-a-disconnection) explains sending a missed signal again.

## Media server does not show an imported file

Confirm the import completed and the file is in the intended library. Then check that the media server can read that storage through its own library configuration. Magpie's root-folder path and the server's path can differ, as long as both refer to the same files. A passing API test does not show that the server's library contains the imported file, so check the refresh job's result too. The [media-server extension instructions](../extensions.md#connecting-services-and-inspecting-files) cover library mapping and refresh behavior.

## Preserving evidence and verifying the correction

When asking for help, include the Magpie and extension versions, execution IDs, time and node, the original error, and the logs around it, with credentials and secrets removed. After a fix, repeat one representative operation and check its final state and the resulting file. A change that replaces database state or files needs the [recovery procedure](maintenance.md) ready first.

---

[Administration](README.md) | [Documentation](../README.md)
