# UI contributions

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

A UI contribution puts an element of your own into a named place in Magpie's interface. That place is a slot, a location where the host permits [extension](../extensions.md) content, and the element is a standard Web Component. The [UI slot reference](../../reference/ui-slots.md) lists the slots and what the element must do.

`IUIExtensionProvider` declares where the element goes, and its JavaScript defines what it does. An [asset provider](ui-assets.md) delivers that script to the browser. Use a contribution for a control or view inside an existing slot, and a [theme](themes.md) for presentation changes that add no behavior.

## Adding an element to library actions

This continues the Scratch extension from [Creating an extension](../extensions.md). Add the provider as `ScratchUiProvider.cs`:

```csharp
using Magpie.Models.Enums;
using Magpie.Models.UI;
using Magpie.Sdk.UI;

namespace Magpie.Extensions.Scratch;

internal sealed class ScratchUiProvider : IUIExtensionProvider
{
    /// <inheritdoc />
    public IEnumerable<UIExtension> GetUIExtensions()
    {
        yield return new()
        {
            Id = new Guid("f849b3cc-c334-46a6-b6b9-cc07ef4a1d46"),
            ExtensionId = ScratchExtension.ExtensionId,
            Name = "Scratch status",
            Slot = "content.list.actions",
            TagName = "scratch-library-status",
            Modifier = nameof(UISlotModifier.After),
            Priority = 100
        };
    }
}
```

Register it in `ScratchExtension.RegisterServices`, with `using Magpie.Sdk.UI;` in the entry-point file:

```csharp
services.AddSingleton<IUIExtensionProvider, ScratchUiProvider>();
```

The provider is stateless and returns declarations synchronously. It shouldn't read per-user state or call a remote service, because the host calls `GetUIExtensions` for each [manifest](ui-assets.md) request. If it throws, the host logs a warning and leaves that provider's declarations out of the manifest.

Then create `Assets/Widget.js`:

```javascript
(() => {
    if (customElements.get('scratch-library-status')) {
        return;
    }

    class ScratchLibraryStatus extends HTMLElement {
        connectedCallback() {
            this.textContent = 'Scratch extension loaded.';
        }
    }

    customElements.define('scratch-library-status', ScratchLibraryStatus);
})();
```

The guard lets the loader run the script again without defining the tag twice. The message only confirms that the browser loaded the script. A declaration alone doesn't define the element, so embed and serve this file as described in [UI assets](ui-assets.md#embedding-and-serving-the-scratch-script). After a restart, the text appears above the library table in **List** view at `/library/all`, on a viewport at least 1024 CSS pixels wide.

## Choosing a slot

The [UI slot reference](../../reference/ui-slots.md) gives exact slot names and their context: ordinary slots, detail tabs, sidebar links, and extension pages.

Ordinary slots support `Before`, `After`, and `Replace`. The browser compares `Modifier` exactly with those names and ignores anything else, including a different letter case. Lower priorities render first, and only the first sorted replacement replaces normal content. Replacing a parent slot also removes its nested built-in slots. Tab and navigation slots accept only `After`, and navigation declarations create host-rendered links without mounting an element.

The example uses `content.list.actions`, an empty region above the library table that carries no selected-item context. Use stable declaration IDs and unique custom-element names with a hyphen. The reference describes the [declaration fields](../../reference/ui-slots.md#declaration-fields) and [composition and fallback behavior](../../reference/ui-slots.md#ordinary-slots).

## Context and cleanup

The host sets [context attributes](../../reference/ui-slots.md#host-attributes-and-lifecycle) on the element for the API base, the owning extension, and the declaration name, plus user and [role attributes](../../reference/roles.md) when available. Entity attributes and other context depend on the slot. `UIExtension.Attributes` adds attributes of your own, and a host context value overrides a declaration attribute of the same name.

Don't assume a selected item in a slot that doesn't provide one, like the list-actions slot above. A detail-page component also has to handle absent or changing context, using `observedAttributes` and `attributeChangedCallback` when behavior depends on attributes that can change while the element stays mounted.

Release listeners, subscriptions, and in-flight work in `disconnectedCallback`, and don't attach duplicates when the element connects again. For authenticated API calls, use the asset context described in [UI assets](ui-assets.md#load-order-and-browser-context). A role attribute can shape presentation, but the [server endpoint](api-endpoints.md#keeping-request-work-in-the-handler) has to enforce access.

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
