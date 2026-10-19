# MagpieQL reference

[Documentation](../README.md) / [Reference](README.md)

MagpieQL is the query language for [content](../using/collection.md) and [subject](../using/collection.md#subjects-and-tags) filters. This page defines its accepted forms and their meaning in the local collection. External [metadata providers](../administration/metadata.md) can support fewer forms. For a worked example, see [Building a collection filter](../power-user/filters-and-magpieql.md#building-a-collection-filter).

- Syntax: [values and expressions](#values-and-expressions), [operators](#operators), [operators by field](#operators-by-field), and [dates and durations](#dates-and-durations).
- Fields: [content fields](#content-fields), [collection matching](#collection-matching), [content ratings](#content-ratings), and [subject queries](#subject-queries).
- Results: [missing values](#missing-values) and [validation and result limits](#validation-and-result-limits).

## Values and expressions

Field names, keywords, and named enum values are case-insensitive. Write enum values without quotes, such as [`Monitored`](../using/monitoring-and-wishlists.md#setting-monitoring). Put text, dates, durations, and GUID identifiers in double quotes.

| Value | Example |
| --- | --- |
| Text | `title = "Harbor Signal"` |
| Integer | `primaryMediaFileCount >= 1` |
| Boolean | `pendingMetadataRefresh = true` |
| Enum | `monitoring = Shotgun` |
| GUID | `parentId = "11111111-1111-1111-1111-111111111111"` |
| Calendar date | `publishDate >= "2025-01-01"` |
| Duration | `duration > "1h30m"` |

Use `\"` for a double quote within a quoted string and `\\` for a literal backslash. Whitespace and line breaks can separate tokens.

`and` requires several conditions, parentheses group expressions, and `and` has precedence over `or`. `or` works only between positive `has` expressions on the same collection (see [collection matching](#collection-matching)). There is no general `not`, so use a negative operator such as `!=`, `!in`, `!contains`, or `!has`.

## Operators

| Operation | Operators | Example and meaning |
| --- | --- | --- |
| Equality | `=`, `!=` | `monitoring != Unmonitored` includes Monitored and Shotgun. |
| Ordered comparison | `>`, `>=`, `<`, `<=` | `primaryMediaSize > 1000000` requires more than 1,000,000 bytes. |
| Text match | `contains`, `startswith`, `endswith` | `title startswith "The "` matches that prefix. |
| Negative text match | `!contains`, `!startswith`, `!endswith` | `title !contains "Orbit"` excludes titles that contain that text. |
| Missing or present | `null`, `!null` | `publishDate !null` requires a resolved publication date. No comparison value follows the operator. |
| Scalar membership | `in`, `!in` | `fulfillment in (Missing, Upgradable)` accepts either value. |
| Collection match | `has`, `!has` | `genres has (name = "Drama")` requires a matching genre entry. |

Boolean fields support only `=` and `!=` with `true` or `false`. Membership lists need one or more non-null values, and absent values are selected with `null`. Collection item fields do not support `in` or `!in`.

Text matching in local [metadata](../power-user/metadata.md) and name fields is case-insensitive. Magpie compares the content `path` field without case folding, but the database collation can still make that comparison case-insensitive. `mediaFiles` `filePath` matching is case-insensitive. Use the content-type or level slug for `contentType` or `level`, not the display label.

### Operators by field

Each field supports only the operators in this table. The text operators are `contains`, `!contains`, `startswith`, `!startswith`, `endswith`, and `!endswith`. The ordered operators are `>`, `>=`, `<`, and `<=`.

| Fields | Supported operators |
| --- | --- |
| `name`, `slug`, `path`, `contentType`, `level`, subject `kind` | `=`, `!=`, text operators, `in`, `!in`, `null`, `!null` |
| Content `title`, `originalTitle` | `=`, `!=`, `contains`, `!contains`, `startswith`, `endswith` |
| Subject `title`, `originalTitle` | `=`, `!=`, text operators, `null`, `!null` |
| `language`, `productionStatus`, subject `overview` | `=`, `!=`, text operators, `null`, `!null` |
| `publishDate`, `birthDate`, `deathDate`, `established`, `dissolved` | `=`, `!=`, ordered operators, `in`, `!in`, `null`, `!null` |
| `duration` | `=`, `!=`, ordered operators, `null`, `!null` |
| `primaryMediaSize`, `primaryMediaFileCount`, `dateLastRefreshed` | `=`, `!=`, ordered operators, `in`, `!in`, `null`, `!null` |
| `rootFolderId`, `parentId` | `=`, `!=`, `in`, `!in`, `null`, `!null` |
| `monitoring`, `fulfillment`, `qualityStatus`, `activeAcquisitionStatus` | `=`, `!=`, `in`, `!in` |
| `pendingMetadataRefresh`, `hasSearchableTitle`, `importPending` | `=`, `!=` |

Validation checks operators only for Boolean fields and collection item fields. For any other field, an unsupported operator can pass validation and then fail when the query runs. [Collection matching](#collection-matching) lists the collection item operators.

## Missing values

`field null` matches absence. `field !null` requires presence. An empty string is not the same as null.

Positive text comparisons and membership require a value. Negative text comparisons and negative membership also match absent values. For example:

```text
language !null and language != "en"
```

This selects items with a resolved language other than `en`. Without the first condition, an item without a language also matches.

State fields have their own behavior:

- `monitoring` matches the effective mode after [inheritance](../administration/configuration.md#understanding-collection-defaults). It does not expose an absent item-level override.
- `fulfillment = Missing` also includes an item that has no content-state record.
- `qualityStatus` and `activeAcquisitionStatus` require a matching stored state for `=` or `in`. Their `!=` and `!in` forms include absent states.
- `primaryMediaSize null` and `primaryMediaFileCount null` mean that Magpie has not yet calculated the media totals, or that the item has no content-state record. Every other comparison, including `!=` and `!in`, matches only calculated totals. A calculated zero is different from an unavailable total.

A `has` expression does not match an empty collection. A `!has` expression matches an empty collection because the collection contains no matching entry.

## Dates and durations

Metadata dates accept these quoted forms: a year (`"2025"`), a month (`"2025-03"`), a day (`"2025-03-15"`), or a timestamp with an explicit time-zone offset (`"2025-03-15T12:00:00Z"`).

A year, month, or day value denotes a period:

| Operator | Comparison with the requested period |
| --- | --- |
| `=` | At or after its start and before the next period. |
| `!=` | Outside that period, including an absent value. |
| `>` | At or after the next period. |
| `>=` | At or after its start. |
| `<` | Before its start. |
| `<=` | Before the next period. |

For example, `publishDate = "2025"` selects dates in 2025, and `publishDate > "2025"` selects dates from 2026 onward. Comparisons use the stored date, which is not an exact day when a source supplies only a year.

Durations accept quoted time spans, such as `"01:30:00"`, or the short units `h`, `m`, and `s`, such as `"1h30m"` or `"90m"`. A quoted number without a unit, such as `"90"`, is a number of days. Digits after the last unit are minutes, so `"1h30"` is 1 hour 30 minutes.

## Content fields

On the library page, a query searches only the current content-type and hierarchy scope.

### Identity and hierarchy

| Field | Type and meaning |
| --- | --- |
| `name` | Stored name of the collection item. It is separate from the resolved metadata title. |
| `slug` | Stored hierarchy-item slug. |
| `path` | Stored path of the collection item. |
| `contentType`, `level` | Content-type and hierarchy-level slugs supplied by a content definition. |
| `rootFolderId` | Explicit [root-folder](../administration/storage-and-services.md#adding-a-root-folder) identifier stored on the item. This is not its inherited effective root folder. |
| `parentId` | Parent hierarchy-item identifier. `parentId null` selects top-level items. |

### Titles

`title` and `originalTitle` match values in any [metadata contribution](../power-user/metadata.md#inspecting-a-conflicting-value), including lower-priority contributions. The displayed value is only one of the values that can match.

For example, one contribution supplies **Harbor Signal** and another supplies **The Harbor Signal**. The item matches `title = "The Harbor Signal"` even when **Harbor Signal** is displayed. It does not match `title != "The Harbor Signal"`. Changing contribution order does not change these results.

A negative comparison requires that no contribution matches the positive comparison. In subject filters, `null` means no contribution has a value, and `!null` requires at least one.

### Resolved metadata

These fields match the first non-null value in contribution order. See [Inspecting a conflicting value](../power-user/metadata.md#inspecting-a-conflicting-value).

| Field | Type and meaning |
| --- | --- |
| `language`, `productionStatus` | Resolved language and production-status text. |
| `publishDate` | Resolved publication date. Accepts calendar periods or an exact timestamp. |
| `duration` | Resolved duration. |

### Refreshing and import state

| Field | Type and meaning |
| --- | --- |
| `pendingMetadataRefresh` | Whether the item is marked for metadata refresh. |
| `hasSearchableTitle` | True when metadata refresh is not pending, or when a contribution already contains a nonempty title. This is not a title-presence check. |
| `importPending` | True when pending library-import work targets the item and the item has no primary media file yet. |

### Media and acquisition state

| Field | Type and meaning |
| --- | --- |
| `primaryMediaFileCount` | Count of primary media files. Null until Magpie calculates the media totals. |
| `primaryMediaSize` | Total size of primary media files, in bytes. Null until Magpie calculates the media totals. |
| `monitoring` | Effective monitoring: `Unmonitored`, `Monitored`, or `Shotgun`. |
| `fulfillment` | Content fulfillment. See the values below. |
| `qualityStatus` | [Quality](../administration/storage-and-services.md#quality-choices) state: `Met` when existing media meets the effective target, or `UpgradeWanted` when an upgrade is wanted. |
| `activeAcquisitionStatus` | Current [acquisition](../using/search-and-download.md#following-completion) state recorded on the content item. See the values below. |

#### Fulfillment values

| Value | Meaning |
| --- | --- |
| `Missing` | No media fulfillment is recorded. |
| `Partial` | Some, but not all, descendant leaves of a container are covered. |
| `Upgradable` | Media exists and the item accepts upgrades. |
| `Fulfilled` | Media exists and no more is required. |
| `FulfilledExternally` | An external source fulfills the item, so acquisition is not required. |

#### Acquisition values

This field filters the current content state. It does not search the download history.

| Value | Meaning |
| --- | --- |
| `Grabbed` | The grab is complete and the client has the download. |
| `Queued` | The download waits in the client queue. |
| `Downloading` | The download is in progress. |
| `Paused` | The download is paused. |
| `Stalled` | The download makes no progress. |
| `Downloaded` | The download is complete. |
| `Importing` | The library import of the download is in progress. |
| `Imported` | The library import of the download is complete. |
| `Failed` | A permanent failure stopped the grab, the download, or the import. |
| `Faulted` | A transient failure stopped the grab, the download, or the import. The release can be grabbed again. |
| `Cancelled` | A user stopped the download. |

## Collection matching

Singular and plural names are aliases, except for `subtree`. Use the same spelling on both sides of an `or` expression.

| Collection | Item fields | Match target |
| --- | --- | --- |
| `genres`, `tags` | `name` | A genre or tag name. |
| `awards` | `body`, `category`, `country`, `language`, `name`, `status`, `year` | One award entry. `year` is an integer. |
| `contentRatings` | `body`, `rating` | One classification from a ratings body. Both fields are strings. See [content ratings](#content-ratings). |
| `externalIds` | `source`, `value` | One provider identifier. |
| `images` | `height`, `width`, `language`, `mimeType`, `source`, `type`, `url` | One image entry. The dimensions are integers. |
| `relationships` | `externalId`, `externalIdSource`, `kind`, `name`, `qualifier`, `relatedMetadataContainerId`, `subject`, `type` | One [relationship](#relationship-fields) and the identity of its related entity. `name` aliases `subject`. |
| `mediaFiles` | `fileKind`, `filePath`, `fileSize`, `mimeType` | A file associated with the item, including a file that covers the item. Size is in bytes. |
| `subtree` | `fulfillment`, `monitoring` | The item itself or a descendant. |

Collection text fields support `=`, `!=`, the text operators, `null`, and `!null`. Numeric item fields support `=`, `!=`, the ordered operators, `null`, and `!null`. These fields have narrower rules:

- `images.url` supports `=`, `!=`, `null`, and `!null`. A supplied URL must be absolute.
- `relationships.relatedMetadataContainerId` supports `=`, `!=`, `null`, and `!null`, with a quoted GUID.
- `relationships.subject` and its `name` alias support `=`, `!=`, `contains`, `!contains`, `startswith`, `endswith`, `null`, and `!null`.
- `subtree.monitoring` and `subtree.fulfillment` support `=` and `!=`.

If several contributions supply an identifier with the same source, `externalIds` matches only the identifier from the first contribution in order.

The dot notation above names a field within its collection. In a query, write the field inside `has (...)`, as in `images has (url = "https://example.org/poster.jpg")`.

A value without parentheses matches the collection's primary field with `=`. For example, `genres has "Drama"` is the same as `genres has (name = "Drama")`. The primary field is `name` for `genres`, `tags`, and `awards`, `value` for `externalIds`, `rating` for `contentRatings`, `type` for `images`, `kind` for `relationships`, `fileKind` for `mediaFiles`, and `monitoring` for `subtree`.

A single `has` requires one entry that satisfies all its conditions:

```text
externalIds has (source = "tmdb" and value = "123")
```

Separate `has` clauses joined with `and` can match different entries:

```text
genres has (name = "Drama") and genres has (name = "Science Fiction")
```

Separate positive `has` clauses on the same collection can use `or`:

```text
genres has (name = "Drama") or genres has (name = "Science Fiction")
```

These forms are not supported:

- `or` between scalar conditions or different collection fields.
- `or` inside one collection item's parentheses.
- `or` combined with `!has`.
- A general `not` expression.

For alternatives on one scalar field, use `in (...)`.

### Content ratings

`contentRatings` matches classifications supplied by a ratings body, such as `PG-13`. The `body` field identifies the ratings body. The `rating` field contains its classification. Both fields contain text.

```text
contentRatings has (rating = "PG-13")
```

This selects items with a stored `PG-13` classification from any ratings body. An item without that classification does not match.

MagpieQL has no numeric review, audience, or critic score field.

### Relationship fields

A relationship's role describes the connection. Its subject kind describes the related entity. For a book's author, the role is `author` and the subject kind is `person`.

| Field | Match target |
| --- | --- |
| `kind` | Role slug for the relationship, such as `author`. |
| `type` | Related subject's kind, such as `person`. For related content, its content-type or hierarchy-level slug also matches. |
| `subject`, `name` | Related entity's name, matched against titles and alternate names in its metadata contributions. Both fields have the same meaning. |
| `qualifier` | Optional detail attached to the relationship's role. |
| `relatedMetadataContainerId` | Local GUID of the metadata container that groups the related entity's contributions. |
| `externalIdSource` | Source name for an identifier on the related entity. |
| `externalId` | Identifier value on the related entity. With `externalIdSource` in the same `has`, both conditions match one identifier. |

A top-level view also applies relationship matches from child items. The library page is a top-level view by default, and so is `GET /api/content` with `topLevelOnly=true`. On such a view, `relationships has (...)` also selects the top-level ancestor of a matching child item, and `relationships !has (...)` also excludes that ancestor. A `parentId null` condition does not apply this rule.

## Subject queries

Subject filters use a separate field set. Content-only fields such as monitoring, fulfillment, media files, and genres do not apply.

| Fields | Meaning |
| --- | --- |
| `kind` | Subject kind supplied by the subject definition. |
| `dateLastRefreshed` | Last recorded subject refresh time. Accepts a quoted date or date and time, and compares exact times, not calendar periods. |
| `title`, `originalTitle` | Values from any contribution, matched as described in [titles](#titles). Subject filters also accept `null`, `!null`, `!startswith`, and `!endswith` for these fields. |
| `overview`, `language` | First non-null value in contribution order. |
| `birthDate`, `deathDate` | Person dates, with calendar-period comparisons. |
| `established`, `dissolved` | Group or organization dates, with calendar-period comparisons. |

Subject collections are `tags`, `awards`, `externalIds`, and `images`, including singular aliases, with the same item fields and rules as above. The editor can offer fewer fields than this page lists.

## Validation and result limits

The parser limits a query to 8,192 characters, 32 parenthesis levels, and 256 `and` or `or` operators. A syntax error includes a position and, where available, the expected input. Field, value, and operator checks can reject an expression after tokenization. The operator checks cover only Boolean fields and collection item fields. See [operators by field](#operators-by-field).

| Error or result | Correction |
| --- | --- |
| `Unknown field: NAME` or `Unknown field NAME in collection COLLECTION` | Check the spelling and whether the query targets content or subjects. |
| `Cannot parse value for field NAME` | Check the field type, quoted form, enum spelling, or date format. |
| `Operator OPERATOR is not supported for field NAME` | Use an operator from [operators by field](#operators-by-field) or [collection matching](#collection-matching). The message uses internal operator names, such as `In`. |
| `Empty value list` | Put at least one value in `in (...)` or `!in (...)`. |
| `Value for field url must be an absolute url` | Use an absolute URL in `images has (url ...)`. |
| `Field NAME is constrained more than once with conflicting values` | The query sets one Boolean field to both `true` and `false`. Remove one condition. |
| `OR expressions are only supported for collection fields on the same collection` | Use the supported combinations in [collection matching](#collection-matching). |
| `The query does not support NOT expressions. Use !has or != instead.` | Use a negative operator. |
| `NAME is a media file field. Use mediaFiles has (NAME ...)` | Put the field inside `mediaFiles has (...)`. |
| **Simple filter** is unavailable, with the tooltip "This query cannot be edited in simple mode" | The query uses options that simple mode cannot show. It stays in **Advanced filter**. |
| The **Switch to Simple Mode** dialog on the **Filters** page | **Switch Mode** clears the query. Select **Cancel** to keep it. |
| Provider warning | Read the provider's message about unsupported criteria or incomplete results before using the result. |

A valid query can still get incomplete results from external providers. See [collection filtering and external discovery](../power-user/filters-and-magpieql.md#collection-filtering-and-external-discovery).

---

[Reference](README.md) | [Documentation](../README.md)
