# Development

[Documentation](../README.md)

Magpie is developed in two ways. You can change the application itself, meaning the host, the API, or the browser UI. Or you can write an [extension](../administration/extensions.md), which adds media types, download sources, connections to other services, and most other features without touching the application. The first-party extensions use the same contracts as yours.

## The application

[Local development](local-setup.md) covers running the host and UI from source. [Application boundaries](architecture.md) describes how the parts fit together. [Building and verifying a release](releases.md) covers the artifacts a release produces. Contributions must follow the [AI Usage Policy](../../AI_POLICY.md).

## Extensions

The three tutorials build one extension, **Scratch directory**:

1. [Creating an extension](extensions.md) builds a package, installs it, and saves a setting.
2. [Adding a Doctor check and repair](diagnostics.md) inspects that setting and repairs a missing directory.
3. [Publishing an extension](publish-extensions.md) declares compatibility and publishes to a feed.

[Extension components](components/README.md) lists what an extension can add or replace. [Using built-in services](using-built-in-services.md) covers calling Magpie from your code. The [extension contract](../reference/extensions.md) and [diagnostic](../reference/diagnostics.md) references hold the exact types.

---

[Documentation](../README.md) | [Magpie overview](../../README.md)
