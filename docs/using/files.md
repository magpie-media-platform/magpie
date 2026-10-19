# Managing files

[Documentation](../README.md) / [Everyday use](README.md)

The **Files** view of a [collection item](collection.md) lists the media files associated with it. Importing, renaming, unlinking, reassigning, and deleting files need [Manager access](../reference/roles.md) or higher.

## Importing existing files

**Library import** matches files that are already in a library root to titles. Downloads use [download import](search-and-download.md#following-completion) instead. Library import scans the path as Magpie sees it, which may differ from the path on the host (see the [storage example](../administration/storage-and-services.md)).

Open **Library import**, scan the root folder, and check the proposed matches. For multi-level content, check the [child item](collection.md#content-types-and-hierarchy) as well as the title. **Match** corrects a wrong match or supplies a missing one. Choose **Accept selected**, pick the options below in the **Accept import** dialog, and select **Accept**. The import runs as a [job](../administration/jobs-and-automation.md#jobs-and-executions). For music and books, matching depends on [album identity](../../src/Magpie.Extensions.MusicBrainz/README.md#matching-an-album-before-importing-files) and [book identity](../../src/Magpie.Extensions.Books/README.md#checking-identity-before-import).

### Choosing monitoring and file names

The import monitoring choice applies only to content the import creates.

| Import monitoring | Result |
| --- | --- |
| Inherit | New content uses [inherited monitoring](../administration/configuration.md#understanding-collection-defaults). |
| Monitored | Monitors the new top-level item and the descendants that inherit from it, so missing children can become eligible for acquisition. |
| Unmonitored | Adds the files and content without monitoring them. |
| Future only | Monitors the new top-level item, including a movie with no future date. New descendants are monitored only if they have a future date. |

Importing keeps the current paths. If the root folder has a [rename mask](../administration/storage-and-services.md#configuring-file-naming), **Apply rename mask** renames the files as part of the import.

## Inspecting and correcting file associations

A title's **Files** view shows paths, sizes, and media details. Missing codec and track details usually mean the [media inspector](../administration/storage-and-services.md#service-roles) is not working.

If a TV file is on the wrong episode, select **Correct episode association** and choose the destination season and episode, which must already be in the library.

### Importing one file

To attach one known file to an existing item, open its **Files** view and select **Import file**, then browse to the file. Confirm with **Associate file** in the dialog. With a rename mask, select **Apply rename mask**, review the preview, and confirm with **Associate and apply valid renames**.

## Applying a naming choice

A rename mask builds paths from content and file information. An administrator [configures the mask](../administration/storage-and-services.md#configuring-file-naming) on the root folder. [Metadata corrections](../power-user/metadata.md#correcting-one-field) change the destination.

Select files and choose **Rename using mask**. The preview compares **Original path**, **Destination path**, and **Outcome** for each file, and **Token values for** and **Available tokens** show where a value comes from. For missing values or invalid destinations, see [token and preview checks](../administration/storage-and-services.md#checking-token-values-and-previewing-failures). **Apply valid renames** renames the valid files even if others have errors.

## Unlinking or deleting a file

| Action | Collection | Disk |
| --- | --- | --- |
| Unlink | Removes the association with the title. | Keeps the file. |
| Delete from disk | Removes the file from the collection. | Requests deletion of the file. |

A file can cover several items, such as multiple episodes. **Unlink** keeps the other associations, while **Delete from disk** removes them all.

An item that stays monitored with no acceptable media can be acquired again unless its [monitoring](monitoring-and-wishlists.md#understanding-wanted-items-and-upgrades) is changed first. To remove a whole title, see [Removing a title](collection.md#removing-a-title). To remove a download and its files, see [Searching and download](search-and-download.md#controlling-or-removing-a-download).

---

[Everyday use](README.md) | [Documentation](../README.md)
