# Theme tokens

[Documentation](../README.md) / [Reference](README.md)

[Theme providers](../development/components/themes.md) supply stylesheets that change shared appearance values through CSS custom properties, called tokens. This page lists the 123 shared base tokens by purpose. A theme overrides only the tokens it wants to change, in `:root`, and the selected Dark or Light variant supplies the rest.

**On this page**

- [Match surfaces and text](#surfaces-text-and-borders)
- [Set accents, filters, and controls](#accents-filters-and-controls)
- [Distinguish status and fulfillment](#status-and-fulfillment-colors)
- [Set typography and spacing](#typography-and-spacing)
- [Set corners, shadows, and overlays](#corners-shadows-and-overlays)
- [Set motion, icons, and layout](#motion-icons-and-layout)
- [Set stacking, opacity, and focus](#stacking-opacity-and-focus)

A value such as `var(--bg-primary)` uses that token's final value, so an override also changes the tokens that depend on it. Components can also have local CSS values and private selectors that are not part of this interface. Where one base value is shown, both variants use the same declaration, though a token reference can still resolve to different colors. All color mixes use the `srgb` color space.

## Surfaces, text, and borders

All values in this section are CSS colors.

| Token | Purpose | Base values |
| --- | --- | --- |
| `--bg-primary` | Primary background | Dark: `#1a1b26`; Light: `#f3f5fa` |
| `--bg-secondary` | Secondary background | Dark: `#24283b`; Light: `#fff` |
| `--bg-tertiary` | Tertiary background | Dark: `#414868`; Light: `#e4e9f3` |
| `--text-primary` | Primary text | Dark: `#c0caf5`; Light: `#202b45` |
| `--text-secondary` | Text with less emphasis | Dark: `#a9b1d6`; Light: `#465675` |
| `--text-muted` | Muted text | Dark: `#858db5`; Light: `#596986` |
| `--text-on-inverse` | Text on an inverse, dark surface | `rgb(255 255 255 / 92%)` |
| `--bg-scrim` | Scrim background behind text | Dark: 55% `--bg-primary`, 45% transparent; Light: `var(--bg-primary)` |
| `--text-on-scrim` | Text on the scrim | Dark: `var(--text-on-inverse)`; Light: `var(--text-primary)` |
| `--border-default` | Default border | Dark: `#414868`; Light: `#c4ccdd` |
| `--border-subtle` | Less prominent border | Dark: `#343b58`; Light: `#dce2ed` |
| `--border-accent` | Accent border | `var(--accent-primary)` |

An inverse surface uses a dark background with light foreground content. A scrim is a background layer that keeps text readable over other content.

## Accents, filters, and controls

All values in this section are CSS colors. Indicator colors and fill colors have separate tokens. Accent fills pair with `--text-on-accent`, and the active-filter fill with `--text-on-filter`.

### Accent and filter colors

| Token | Purpose | Base values |
| --- | --- | --- |
| `--accent-primary` | Primary accent for text and indicators | Dark: `#7aa2f7`; Light: `#315cc5` |
| `--accent-secondary` | Secondary accent | Dark: `#7dcfff`; Light: `#2256a8` |
| `--accent-fill` | Primary accent fill | `var(--accent-primary)` |
| `--accent-fill-hover` | Hover accent fill | 85% `--accent-fill`, 15% white |
| `--accent-secondary-fill` | Secondary accent fill | Dark: `var(--accent-secondary)`; Light: `var(--accent-fill-hover)` |
| `--text-on-accent` | Foreground on accent fills | Dark: `var(--bg-primary)`; Light: `#fff` |
| `--filter-active` | Active-filter indicator | Dark: `#bb9af7`; Light: `#7146ad` |
| `--filter-active-fill` | Active-filter fill | `var(--filter-active)` |
| `--text-on-filter` | Foreground on the active-filter fill | `var(--bg-primary)` |

`--accent-primary` also feeds text, fills, borders, shadows, and focus indicators through dependent base values.

### Button and switch colors

| Token | Purpose | Base values |
| --- | --- | --- |
| `--button-primary-fg` | Primary button foreground | Dark: `#fff`; Light: `var(--text-on-accent)` |
| `--button-destructive-bg` | Destructive button background | `var(--status-unhealthy)` |
| `--button-destructive-text` | Foreground on destructive buttons | Dark: `var(--bg-primary)`; Light: `#fff` |
| `--button-warning-bg` | Warning button background | `var(--status-degraded)` |
| `--button-warning-text` | Foreground on warning buttons | Dark: `var(--bg-primary)`; Light: `#fff` |
| `--switch-thumb` | Switch thumb | Dark: `var(--text-primary)`; Light: `#fff` |
| `--switch-thumb-error` | Switch thumb in an error state | Dark: `var(--switch-thumb)`; Light: `var(--button-destructive-text)` |

## Status and fulfillment colors

All values in this section are CSS colors.

### Health and live connection

| Token | Purpose | Base values |
| --- | --- | --- |
| `--status-healthy` | Healthy status | Dark: `#9ece6a`; Light: `#367320` |
| `--status-unhealthy` | Unhealthy status | Dark: `#f7768e`; Light: `#b82b47` |
| `--status-degraded` | Degraded status | Dark: `#e0af68`; Light: `#805708` |
| `--status-degraded-bg` | Degraded background tint | 10% `--status-degraded`, 90% transparent |
| `--status-degraded-border` | Degraded border tint | 40% `--status-degraded`, 60% transparent |
| `--status-unknown` | Unknown status | `var(--text-muted)` |
| `--status-stabilizing` | Stabilizing status | Dark: `#bb9af7`; Light: `#7146ad` |
| `--status-live-connecting` | Live-connection indicator while connecting | `#eab308` |
| `--status-live-connected` | Live-connection indicator while connected | `#22c55e` |
| `--status-live-failed` | Live-connection indicator after failure | `#ef4444` |
| `--status-live-unsupported` | Indicator for an unsupported live connection | `#000` |

### Fulfillment

[Fulfillment](../using/monitoring-and-wishlists.md#understanding-wanted-items-and-upgrades) describes how much required media is present. These tokens color the [fulfillment states](magpieql.md#fulfillment-values).

| Token | Purpose | Base values |
| --- | --- | --- |
| `--fulfillment-missing` | Missing state | `var(--status-unhealthy)` |
| `--fulfillment-upgradable` | Upgradable state | `var(--status-degraded)` |
| `--fulfillment-fulfilled` | Fulfilled state | `var(--status-healthy)` |
| `--fulfillment-external` | Externally fulfilled state | Dark: `#7dcfff`; Light: `#14678b` |
| `--fulfillment-partial` | Partly fulfilled state | `var(--status-stabilizing)` |

## Typography and spacing

These base values are the same in Dark and Light.

### Font families

Values are CSS font-family lists.

| Token | Purpose and base value |
| --- | --- |
| `--font-sans` | General text: `'Outfit', system-ui, -apple-system, 'Segoe UI', roboto, sans-serif`. |
| `--font-mono` | Monospaced text: `'JetBrains Mono', ui-monospace, 'Cascadia Code', consolas, monospace`. |

### Text sizes

Values are CSS font sizes. The base scale uses `rem` lengths.

| Token | Base value |
| --- | --- |
| `--text-2xs` | `0.65rem` |
| `--text-xs` | `0.7rem` |
| `--text-sm` | `0.8rem` |
| `--text-base` | `0.85rem` |
| `--text-lg` | `1rem` |
| `--text-xl` | `1.25rem` |
| `--text-2xl` | `1.5rem` |
| `--text-2-5xl` | `1.75rem` |
| `--text-3xl` | `2rem` |

### Weight, line height, and letter spacing

| Token | Purpose and CSS type | Base value |
| --- | --- | --- |
| `--weight-normal` | Normal font weight; number | `400` |
| `--weight-medium` | Medium font weight; number | `500` |
| `--weight-semibold` | Semibold font weight; number | `600` |
| `--weight-bold` | Bold font weight; number | `700` |
| `--leading-tight` | Tight line height; unitless multiplier | `1.25` |
| `--leading-normal` | Normal line height; unitless multiplier | `1.5` |
| `--leading-relaxed` | Relaxed line height; unitless multiplier | `1.75` |
| `--tracking-wide` | Wide letter spacing; length | `0.05em` |

### Space between elements

Values are CSS lengths for spacing. The base scale uses `rem` lengths.

| Token | Base value |
| --- | --- |
| `--space-2xs` | `0.125rem` |
| `--space-xs` | `0.25rem` |
| `--space-sm` | `0.5rem` |
| `--space-2-5` | `0.625rem` |
| `--space-md` | `1rem` |
| `--space-lg` | `1.5rem` |
| `--space-xl` | `2rem` |
| `--space-2xl` | `3rem` |

## Corners, shadows, and overlays

### Corner radius

Values are CSS lengths. Both variants use the same base values.

| Token | Purpose | Base value |
| --- | --- | --- |
| `--radius-xs` | Extra-small corner radius | `3px` |
| `--radius-sm` | Small corner radius | `6px` |
| `--radius-md` | Medium corner radius | `10px` |
| `--radius-lg` | Large corner radius | `16px` |
| `--radius-full` | Fully rounded corners | `9999px` |

### Shadows and overlays

Shadow values contain horizontal offset, vertical offset, blur radius, and color. The table lists lengths and color separately.

| Token | Purpose and CSS type | Base values |
| --- | --- | --- |
| `--shadow-sm` | Small shadow; CSS shadow value | Lengths: `0 1px 2px`. Dark: `rgb(0 0 0 / 30%)`; Light: `rgb(32 43 69 / 8%)`. |
| `--shadow-md` | Medium shadow; CSS shadow value | Lengths: `0 4px 16px`. Dark: `rgb(0 0 0 / 35%)`; Light: `rgb(32 43 69 / 10%)`. |
| `--shadow-lg` | Large shadow; CSS shadow value | Lengths: `0 8px 32px`. Dark: `rgb(0 0 0 / 40%)`; Light: `rgb(32 43 69 / 12%)`. |
| `--shadow-xl` | Extra-large shadow; CSS shadow value | Lengths: `0 12px 48px`. Dark: `rgb(0 0 0 / 45%)`; Light: `rgb(32 43 69 / 16%)`. |
| `--shadow-glow` | Accent glow; CSS shadow value | Lengths: `0 0 16px`. Color: `--accent-primary` mixed with transparency; Dark: 30% accent, Light: 20% accent. |
| `--glow-error` | Error glow; CSS shadow value | Lengths: `0 0 16px`. Color: `--status-unhealthy` mixed with transparency; Dark: 30% status color, Light: 20% status color. |
| `--overlay-backdrop` | Background behind an overlay; CSS color | Dark: `rgb(0 0 0 / 70%)`; Light: `rgb(20 29 49 / 45%)`. |
| `--blur-overlay` | Overlay blur radius; CSS length | `4px` |

The two variants use different shadow and backdrop colors.

## Motion, icons, and layout

These base values are the same in Dark and Light.

### Motion

| Token | Purpose and CSS type | Base value |
| --- | --- | --- |
| `--duration-fast` | Fast transition; time | `150ms` |
| `--duration-normal` | Normal transition; time | `200ms` |
| `--duration-slow` | Slow transition; time | `300ms` |
| `--duration-slower` | Slower transition; time | `400ms` |
| `--duration-spinner` | Spinner cycle; time | `750ms` |
| `--duration-pulse` | Pulse cycle; time | `1s` |
| `--easing-default` | Default timing function | `cubic-bezier(0.4, 0, 0.2, 1)` |

### Icons and indicators

Values are CSS lengths. The general and inline icon scales have different sizes.

| Token | Purpose | Base value |
| --- | --- | --- |
| `--icon-size-xs` | Extra-small icon | `10px` |
| `--icon-size-sm` | Small icon | `2rem` |
| `--icon-size-md` | Medium icon | `3rem` |
| `--icon-size-lg` | Large icon | `4rem` |
| `--icon-inline-xs` | Extra-small inline icon | `16px` |
| `--icon-inline-sm` | Small inline icon | `20px` |
| `--icon-inline-md` | Medium inline icon | `24px` |
| `--icon-logo` | Logo icon | `36px` |
| `--dot-size-sm` | Small status dot | `8px` |

### Layout dimensions

Values are CSS lengths.

| Token | Purpose | Base value |
| --- | --- | --- |
| `--content-max-width` | Maximum content width | `1200px` |
| `--touch-target-min` | Minimum touch target dimension | `44px` |
| `--bottom-nav-height` | Bottom navigation height | `56px` |
| `--header-height` | Header height | `56px` |
| `--sidebar-width` | Expanded sidebar width | `240px` |
| `--sidebar-collapsed-width` | Collapsed sidebar width | `64px` |

## Stacking, opacity, and focus

### Stacking order

Values are integers for CSS `z-index`. Both variants use the same order.

| Token | Purpose | Base value |
| --- | --- | --- |
| `--z-raised` | Raised element | `1` |
| `--z-dropdown` | Dropdown | `100` |
| `--z-sticky` | Sticky element | `200` |
| `--z-overlay` | Overlay | `300` |
| `--z-modal` | Modal | `400` |
| `--z-popover` | Popover | `450` |
| `--z-toast` | Toast notification | `500` |
| `--z-tooltip` | Tooltip | `600` |
| `--z-max` | Highest level in the base scale | `9999` |

### Opacity

Values are unitless numbers from `0` for transparent to `1` for opaque. Both variants use the same base values.

| Token | Purpose | Base value |
| --- | --- | --- |
| `--meter-fill-opacity` | Meter fill opacity | `1` |
| `--opacity-disabled` | Disabled element opacity | `0.5` |
| `--opacity-muted` | Muted element opacity | `0.65` |
| `--opacity-secondary` | Secondary element opacity | `0.7` |
| `--opacity-reduced` | Reduced element opacity | `0.75` |

### Focus indicators

| Token | Purpose and CSS type | Base value in both variants |
| --- | --- | --- |
| `--focus-ring-width` | Focus ring width; length | `2px` |
| `--focus-ring-offset` | Gap between the focused element and ring; length | `2px` |
| `--focus-ring-color` | Focus ring color; CSS color | `var(--accent-primary)` |

To test a theme in both variants, see the [verification checklist](../development/components/themes.md#verifying-selection-and-fallback).

---

[Theme provider tutorial](../development/components/themes.md) | [Reference](README.md) | [Documentation](../README.md)
