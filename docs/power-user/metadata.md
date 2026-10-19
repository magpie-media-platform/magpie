# Managing metadata

[Documentation](../README.md) / [Power-user guide](README.md)

Each [metadata provider](../administration/metadata.md) supplies a set of values called a contribution. The [collection](../using/collection.md) shows the first contribution that has a value for each field. Manual edits make a user contribution that always comes first. Inspecting metadata needs [Viewer access](../reference/roles.md), and editing needs Manager access.

## Inspecting a conflicting value

Open an item's **Metadata** tab. **Properties** shows each resolved value and its source, and the **Contribution stack** shows the contributions in order.

| Contribution order | Title | Overview |
| --- | --- | --- |
| Provider A, first | Harbor Signal | No value |
| Provider B, second | The Harbor Signal | A message reaches a coastal station. |

Here the title is Harbor Signal, and the overview comes from Provider B because Provider A has none. To see whether a provider is stale, find the latest [refresh](#verifying-a-metadata-refresh) in [job history](personal-automation.md#following-an-execution).

## Correcting one field

Select the edit action for a field in **Properties**, enter the value, and select **Apply**. **Save changes** stores your edits. In the example, a manual title of "Harbor Signal - Restored Edition" wins over both providers, and refreshes do not replace it. If the inspector reports another change while you were editing, saving overwrites it.

## Preferring another provider

To let a different provider win conflicts for one item, reorder the provider contributions. The user contribution stays first. In the example, moving Provider B above A makes "The Harbor Signal" the title.

The new order is saved immediately. **Revert** discards unsaved field edits only and does not undo a reorder. The order applies to this item only.

## Understanding values with several entries

Genres and images combine contributions instead of picking one.

| Collection | Resolution |
| --- | --- |
| Genres | Different names are combined. A repeated name comes from the earlier contribution. |
| External identifiers | For each source, the earlier contribution supplies that source's values. |
| Images | For each image type, the earlier contribution supplies the images. |
| Content ratings | For each rating body, the earlier contribution supplies the ratings. |

A manual **Drama** genre does not remove a provider's **Science Fiction**. A wrong external identifier changes what later refreshes fetch.

## Removing a manual correction

To return a field to provider values, edit it, clear the manual value, and select **Save changes**. The field then uses a provider value.

**Clear user contribution** marks all manual fields and entries for removal until you **Save changes**. **Revert** discards it.

## Verifying a metadata refresh

Request a refresh from the item's actions, or run a permitted job in [My Jobs](personal-automation.md). When it finishes, reopen the inspector and compare the provider's latest contribution with the resolved value. If the provider returned nothing or failed, the job message says why. A refresh changes metadata only, not [files or downloads](../using/search-and-download.md#following-completion).

---

[Power-user guide](README.md) | [Documentation](../README.md)
