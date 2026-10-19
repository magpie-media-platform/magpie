# Themes

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

A theme provider adds named dark or light stylesheets to Magpie's [appearance controls](../../using/appearance.md#selecting-a-theme). Each stylesheet is embedded in the provider's assembly. [Tokens](../../reference/theme-tokens.md) are CSS custom properties that name shared appearance values such as colors. The browser starts from the selected variant's base tokens and applies the theme's overrides on top.

Write one with `IThemeProvider` when you want an appearance that users can pick and administrators can set as an [instance default](../../administration/configuration.md#theme-defaults). A theme needs no [UI contribution](ui-contributions.md) or [asset provider](ui-assets.md).

## Dark and light variants

These examples continue the [Scratch extension project](../extensions.md), and the same files work in any extension. Create `Themes/ForestDark.css`:

```css
:root {
    --bg-primary: #101a16;
    --bg-secondary: #18271f;
    --text-primary: #e2eee5;
    --accent-primary: #a0bea4;
}
```

And `Themes/ForestLight.css`:

```css
:root {
    --bg-primary: #f3f8f3;
    --bg-secondary: #e4eee5;
    --text-primary: #183522;
    --accent-primary: #356b46;
}
```

No override is mandatory. A token you leave out keeps the variant's base value. The [theme token reference](../../reference/theme-tokens.md) lists the colors, spacing, controls, and states you can override. Change related foreground and background colors together so text stays legible.

Embed both files with exact resource names:

```xml
<ItemGroup>
  <EmbeddedResource Include="Themes/ForestDark.css" LogicalName="Scratch.Forest.Dark.css" />
  <EmbeddedResource Include="Themes/ForestLight.css" LogicalName="Scratch.Forest.Light.css" />
</ItemGroup>
```

The provider, as `ScratchThemes.cs`:

```csharp
using Magpie.Models.UI;
using Magpie.Sdk.UI;

namespace Magpie.Extensions.Scratch;

internal sealed class ScratchThemes : IThemeProvider
{
    /// <inheritdoc />
    public IEnumerable<ThemeDefinition> GetThemes()
    {
        yield return new()
        {
            Name = "Forest",
            FileName = "Scratch.Forest.Dark.css",
            Variant = ThemeVariant.Dark
        };
        yield return new()
        {
            Name = "Forest",
            FileName = "Scratch.Forest.Light.css",
            Variant = ThemeVariant.Light
        };
    }
}
```

Register it in `ScratchExtension.RegisterServices`, and add `using Magpie.Sdk.UI;` to the entry-point file:

```csharp
services.AddSingleton<IThemeProvider, ScratchThemes>();
```

The provider is stateless and returns its declarations synchronously.

## Catalog identity and resources

The host builds a catalog from the registered providers. A definition needs a non-empty name, a `Dark` or `Light` variant, and an embedded resource whose name ends in `.css`. `FileName` is the exact embedded resource name, not a disk path or URL. The host logs and skips an invalid definition.

A theme ID combines the provider's assembly name, the theme name, and the variant, so the dark theme above is `Magpie.Extensions.Scratch/Forest/Dark`. Keep those parts stable so saved selections still resolve. If two definitions have the same ID, the catalog keeps the first and logs the duplicate.

The shared display name pairs a dark and a light variant, though each is its own catalog entry and stylesheet. An extension can supply just one variant, and users then pick another theme for the other mode.

The theme catalog and stylesheet endpoints allow anonymous access so the appearance applies before sign-in. Never put secrets or user-specific data in theme resources.

## Verifying selection and fallback

Users choose a theme for each variant in the account menu's **Theme** dialog, and **Mode** decides whether dark, light, the device preference, or the administrator's default applies. The [appearance guide](../../using/appearance.md) explains these scopes.

If a selected theme isn't in the catalog or its stylesheet fails to load, Magpie falls back to the built-in Tokyo Night dark theme. The dialog then shows `Selected theme is unavailable. Using Tokyo Night.` or `Could not load Forest. Using Tokyo Night.`

A catalog entry only shows that the host found the resource in the assembly, so check the CSS and the legibility of each component in both variants yourself.

See [IThemeProvider](../../../src/Magpie.Sdk/UI/IThemeProvider.cs), [ThemeDefinition](../../../src/Magpie.Models/UI/ThemeDefinition.cs), and the [theme catalog](../../../src/Magpie.Api/UI/ThemeCatalog.cs).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
