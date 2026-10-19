# UI slots

[Documentation](../README.md) / [Reference](README.md)

A [UI contribution](../development/components/ui-contributions.md) puts an extension's content into a named slot in the interface. The content is a standard [Web Component](https://developer.mozilla.org/en-US/docs/Web/API/Web_components/Using_custom_elements), an autonomous custom element registered with `customElements.define`, so no UI framework is needed.

The slot name selects the location and behavior. Ordinary slots mount elements around built-in content, tab slots add tabs, and navigation slots create links without mounting an element.

**On this page**

- [Declare a contribution](#declaration-fields)
- [Select an ordinary slot](#ordinary-slots)
- [Read gallery context](#gallery-poster-context)
- [Add a detail tab](#detail-tabs)
- [Add a navigation link](#navigation-links)
- [Add an extension page](#extension-pages)
- [Read host attributes and handle lifecycle changes](#host-attributes-and-lifecycle)

## Declaration fields

`IUIExtensionProvider.GetUIExtensions` returns `UIExtension` declarations. An [asset provider](../development/components/ui-assets.md) supplies the JavaScript that registers each rendered element.

| Field | Meaning |
| --- | --- |
| `Id` | Required stable GUID for this declaration. |
| `ExtensionId` | Required GUID of the owning extension. |
| `Name` | Required declaration name. Also supplies the fallback tab and navigation labels. |
| `Slot` | Required exact slot name from this reference. |
| `TagName` | Required custom-element tag name. Use a unique, valid custom-element name that contains a hyphen. The browser ignores a declaration whose tag name has no hyphen. Navigation declarations also require this field, but the host does not mount their element. |
| `Modifier` | A case-sensitive string. Defaults to `After`. Ordinary slots accept `Before`, `After`, and `Replace`. Tab and navigation slots accept only `After`. The browser ignores other values. |
| `Priority` | Integer. Defaults to `0`. Sorts declarations in ascending order, so lower values come first. Do not depend on equal priorities to select a replacement. |
| `Attributes` | Optional dictionary of string names and values. Supplies element attributes, or the special tab and navigation values below. |

For a complete example, see [Adding an element to library actions](../development/components/ui-contributions.md#adding-an-element-to-library-actions).

## Ordinary slots

The browser matches the exact `Slot` value. It groups matching declarations by modifier and orders each group by priority.

| Modifier | Effect |
| --- | --- |
| `Before` | Mount before the slot's normal content. |
| `After` | Mount after the slot's normal content. |
| `Replace` | Replace the slot's normal content with the first sorted replacement. Other replacements are ignored. |

Normal content remains while assets load or fail, or while the selected replacement tag is undefined.

Replacing a parent slot also removes the built-in slots inside it:

- `global.header` contains the built-in header controls, `header.left`, and `header.right`.
- `content.list` contains `content.list.actions`, `content.list.columns`, and `content.gallery.poster`.
- `dashboard.main` contains `dashboard.widgets`.
- `content.details.header` contains the header copy of `content.details.actions`.

Slots exist only on their applicable pages and where the current user's [permissions](roles.md) permit those pages or controls. All mounted elements receive the [common host attributes](#host-attributes-and-lifecycle).

### Slots without entity context

These slots supply no entity ID, entity type, or additional slot context.

| Slot | Location |
| --- | --- |
| `global.header` | Entire application shell header. |
| `header.left` | Left area inside the built-in application header. |
| `header.right` | Right area inside the built-in application header. |
| `dashboard.main` | Main dashboard content. |
| `dashboard.widgets` | Region for additional dashboard widgets. |
| `calendar.main` | Main calendar content. |
| `content.list` | Library page body: the page header, the **List** and **Gallery** view switch, filters, and the list or gallery. |
| `content.list.actions` | Empty extension region above a content table. |
| `content.list.columns` | Empty extension region above a content table. Does not add table column definitions or receive row context. |
| `subject.list` | Subject list. |
| `system.status` | System status content. |
| `admin.backups` | Content of the **Database** administration page, when database backups are available. |
| `admin.extensions` | Content in the built-in **Extensions** administration screen. Use an [extension page](#extension-pages) for a separate page. |

The two empty content-table regions can contain controls or other elements. Neither region supplies a selected item.

These regions appear in two places:

- The library in **List** view. At viewport widths below 1024 CSS pixels, a populated library uses mobile cards and hides the table and these regions. An empty table remains visible. **Gallery** view does not mount either region.
- The **Content** tab of a subject detail page, at viewport widths of 1024 CSS pixels and more. The regions do not receive the subject ID.

`global.header`, `header.left`, and `header.right` do not appear on administration pages, which use a separate header.

### Slots with entity context

`data-magpie-entity-id` identifies the current [collection item](../using/collection.md#content-types-and-hierarchy) or [subject](../using/collection.md#subjects-and-tags). The table specifies `data-magpie-entity-type` for each slot.

| Slot | Location | Entity ID and type |
| --- | --- | --- |
| `content.details.header` | Content detail header. | Current `ContentHierarchyItem.Id`; `content`. |
| `content.details.actions` | Actions in both the detail header and mobile action sheet. | Current `ContentHierarchyItem.Id`; `content`. |
| `content.details.metadata` | Empty region below the content detail header. | Current `ContentHierarchyItem.Id`; `content`. |
| `content.details.overview` | Facts in the **Details** tab. | Current `ContentHierarchyItem.Id`; `content`. |
| `content.details.files` | Content of the **Files** tab. | Current `ContentHierarchyItem.Id`; `content`. |
| `subject.details.header` | Subject detail header. | Current `Subject.Id`; `subject`. |
| `content.gallery.poster` | Poster in each library gallery tile. | Current `ContentHierarchyItem.Id`; no entity type. |

The same declaration can mount in several locations or tiles. Each mounted element must manage its own state and cleanup.

## Gallery poster context

`content.gallery.poster` receives the following attributes in addition to its entity ID and common host attributes. Empty or undefined context values are omitted.

| Attribute | Value |
| --- | --- |
| `data-magpie-level` | Hierarchy-level slug of the tile's item. |
| `data-magpie-poster-url` | Poster URL, when available. |
| `data-magpie-declares-previews` | Boolean string that indicates whether the current level declares preview metadata. |
| `data-magpie-selection-mode` | Boolean string that indicates whether gallery selection mode is active. |
| `data-magpie-preview-url` | URL from the first preview, when available. |
| `data-magpie-preview-kind` | Kind from the first preview, when available. |
| `data-magpie-preview-title` | Title from the first preview, when available. |
| `data-magpie-preview-embed-kind` | Embed kind from the first preview, when available. |

Boolean attributes arrive as strings and are read by comparing the value to `'true'`, because the string `'false'` is truthy in JavaScript. Gallery context is specific to this slot. Detail tabs and extension pages do not receive it.

## Detail tabs

These slots accept only `Modifier = "After"`. The host ignores `Before` and `Replace` declarations.

| Slot | Location | Entity attributes |
| --- | --- | --- |
| `content.details.tabs` | Additional content detail tab. | Current `ContentHierarchyItem.Id` and type `content`. |
| `subject.details.tabs` | Additional subject detail tab. | Current `Subject.Id` and type `subject`. |

When `Attributes["tab-label"]` is supplied, the host uses it as the tab label. Otherwise, the host uses `Name`. The tab uses the host's puzzle-piece icon. The custom element mounts inside the tab content and receives common host attributes and the listed entity attributes.

## Navigation links

These slots accept only `Modifier = "After"`. The host ignores `Before` and `Replace` declarations. It renders sidebar links and does not mount the declared `TagName`.

| Slot | Location |
| --- | --- |
| `nav.main` | **Extensions** group of the application sidebar. |
| `nav.admin` | **Extensions** group of the administration sidebar, after the built-in **Extensions** item. |

| `Attributes` entry | Meaning |
| --- | --- |
| `href` | Required site-relative destination starting with a single `/`. Protocol-relative paths such as `//example.com` and destinations outside the current origin are rejected. Query strings and fragments are permitted. |
| `label` | Optional link text. Defaults to `Name`. |
| `icon` | Optional [Phosphor](https://phosphoricons.com/) icon name, such as `database`. The browser loads the icon from `cdn.jsdelivr.net`. Without a usable name, or when the icon cannot load, the link shows a cube icon. |

Normal route and server permissions still apply. A navigation declaration does not grant access to its destination.

## Extension pages

An extension page is an ordinary slot selected by its URL. It supports `Before`, `After`, and `Replace`, and receives common host attributes without entity or gallery context.

| Page URL | Slot | Host layout |
| --- | --- | --- |
| `/ext/{slug}` | `route.{slug}` | Application page. |
| `/admin/ext/{slug}` | `admin.route.{slug}` | Page inside the administration layout. Requires the Admin role. |

Paths below the page URL, such as `/ext/scratch/item/1`, show the same slot and title. The element reads the browser location itself. When no declaration uses the slot, the host shows the message "This extension does not provide a page here."

The page URL and slot use the same slug. A separate navigation declaration makes the page discoverable:

| Page slot example | Navigation slot | Navigation `Attributes["href"]` |
| --- | --- | --- |
| `route.scratch` | `nav.main` | `/ext/scratch` |
| `admin.route.scratch` | `nav.admin` | `/admin/ext/scratch` |

For a `/ext/{slug}` page, the host reads `nav.main`. For a `/admin/ext/{slug}` page, it reads `nav.admin`. It uses the first declaration, in priority order, whose `href` equals the page URL. The `href` can have a query string or fragment after the URL. The host ignores surrounding whitespace.

The navigation label supplies the page title, with `Name` as its fallback. Without a matching declaration, the title uses the slug. The host adds the word Settings to an administration page title, for example **Scratch Settings**.

An element on an extension page can dispatch a `magpie-unsaved-changes` custom event with `detail: { dirty: true }` when changes are pending, and `detail: { dirty: false }` when they are saved or discarded. The event needs `bubbles: true`, and `composed: true` from inside a shadow root. While any event target has pending changes, the host shows an **Unsaved Changes** dialog before in-app navigation, and the browser asks for confirmation before the page closes or reloads. Other slots ignore this event.

## Host attributes and lifecycle

All custom elements mounted by component slots receive these attributes. Navigation links do not mount custom elements.

| Attribute | Value |
| --- | --- |
| `data-magpie-api-base` | API base URL supplied by the host. |
| `data-magpie-extension-id` | Owning `ExtensionId` from the declaration. |
| `data-magpie-extension-name` | Declaration `Name`, which can differ from the package name. |
| `data-magpie-user-id` | Current user ID, when available. |
| `data-magpie-username` | Current username, when available. |
| `data-magpie-role` | Current role, when available. |
| `data-magpie-entity-id` | Entity ID only in the slots listed above. |
| `data-magpie-entity-type` | Entity type only in the slots listed above. |

The host copies `Attributes` first. Supplied host and slot-context attributes override dictionary values with the same names. Keep extension-specific attributes outside the `data-magpie-*` namespace.

Attributes can change while the element is mounted, so use `observedAttributes` and `attributeChangedCallback` for values the component depends on, and clean up in `disconnectedCallback`.

For authenticated API calls, use the [asset loading context](../development/components/ui-assets.md#load-order-and-browser-context). The user and role attributes are for presentation only, and the server enforces authorization.

---

[Build a UI contribution](../development/components/ui-contributions.md) | [Reference](README.md) | [Documentation](../README.md)
