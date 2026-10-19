# Searching and download

[Documentation](../README.md) / [Everyday use](README.md)

A release search asks [indexers](../administration/storage-and-services.md#service-roles) for downloads of a [title](collection.md#adding-a-title). Each result is a release with its own [quality](../administration/storage-and-services.md#quality-choices) and size. Grabbing a release hands it to a [download client](../administration/storage-and-services.md#service-roles). This needs an indexer, a download client, and a [root folder](../administration/storage-and-services.md#adding-a-root-folder), which an administrator sets up in [Storage and services](../administration/storage-and-services.md).

A title has two release actions.

| Action | Effect | Minimum role |
| --- | --- | --- |
| **Interactive Search** | Shows candidate releases for a person to pick and grab. | [Requester](../reference/roles.md) |
| **Search releases** | Searches the indexers and grabs the best release automatically. | Manager |

**Grab** on the title's **Releases** tab, which lists releases from earlier searches, also needs Manager access. Requesters should use **Interactive Search**.

## Selecting a release

Open the title and select **Interactive Search**. Each result shows the release name, quality details, and any rejection reasons. For a [show](collection.md#content-types-and-hierarchy), a release can cover an [episode or a season pack](../../src/Magpie.Extensions.Tv/README.md#selecting-the-acquisition-scope). Select **Grab** on a suitable release and follow it in **Downloads**. To have Magpie choose releases itself, use [monitoring](monitoring-and-wishlists.md#setting-monitoring).

## Interpreting an unsuccessful search

If an indexer fails, the page shows a warning and still lists releases from the others. Once the indexer recovers, select **Search again**.

| Result | What to look at |
| --- | --- |
| No releases returned | The title and child item searched, and any indexer error. |
| Releases rejected | The rejection reason, compared with the match and quality requirements. |
| Grab failed | The request or [job](../administration/jobs-and-automation.md#jobs-and-executions) message. A download-client problem is not a title-matching problem. |
| Grab accepted but no file | The transfer and [import](#following-completion) stages below. |

## Following completion

Import puts downloaded files in the library and associates them with the title. **Downloads** shows the download state.

| State | Meaning |
| --- | --- |
| Grabbed | The download client has the download. |
| Queued | The download waits in the client queue. |
| Downloading | The transfer is in progress. |
| Paused | The download is paused. |
| Stalled | The download makes no progress, which usually points to the download client. |
| Downloaded | The transfer is complete. Import still has to find and process the files. |
| Importing | Magpie is importing the files into the collection. |
| Imported | The import is complete, and the title's **Files** view shows the files. |
| Faulted | A temporary failure stopped the grab, download, or import. The release can be grabbed again. |
| Failed | A permanent failure stopped the grab, download, or import. The message gives the reason. |
| Cancelled | A person stopped the download. |

Import works only if Magpie can read the location where the client saved the file. If the transfer completed but import failed, the job message shows the path the client reported and the path Magpie tried to read. An administrator can compare them with the [storage mapping](../administration/storage-and-services.md) and [troubleshooting](../administration/troubleshooting.md).

After import, an **Upgradable** [fulfillment state](monitoring-and-wishlists.md#understanding-wanted-items-and-upgrades) means the file is below the quality target.

## Controlling or removing a download

| Action | Effect | Minimum role |
| --- | --- | --- |
| **Pause**, **Resume** | Pauses or resumes the transfer, if the client supports it. | Requester |
| **Force start** | Starts the download immediately, ahead of the queue. | Manager |
| **Stop** | Stops the transfer. It stays in history as **Cancelled** and its files stay on disk. | Manager |
| **Forget** | Removes Magpie's record of the download. The download and files stay in the client. | Manager |
| **Remove** | Removes the download from the client and deletes its files and record. | Manager |

To remove an imported library file, use the title's [file actions](files.md#unlinking-or-deleting-a-file). To remove the title and its descendants, use [Remove from library](collection.md#removing-a-title).

---

[Everyday use](README.md) | [Documentation](../README.md)
