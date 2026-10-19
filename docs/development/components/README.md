# Extension components

[Documentation](../../README.md) / [Development](../README.md)

A component is a piece of behavior that Magpie calls through a [contract](../../reference/extensions.md), such as a metadata provider, a download client, or a theme. An extension supplies components by registering them in its entry point. [Creating an extension](../extensions.md) walks through a working package, and [built-in services](../using-built-in-services.md) covers what you can use without writing a component.

Components come in two kinds. [Additional components](#additional-components) sit alongside others of their kind, and [replacement components](#replacement-components) take over something Magpie already does.

## Additional components

Additional components register next to other implementations. Depending on the component, Magpie calls one, several, or all of them for a request, and each page explains which.

### Defining and enriching content

- [Content type definitions](media-types.md): define collection structure and file behavior.
- [Subject type definitions](subject-types.md): define related entity kinds and relationship roles.
- [Metadata providers](metadata-providers.md): supply content and subject information.
- [Metadata search](metadata-search.md): discover titles outside the local collection.
- [Metadata relationships](metadata-relationships.md): supply subjects related to content.
- [Content facts](content-facts.md): supply identities used in release searches and release matching.

### Find, download, and inspect media

- [Indexer integrations](indexers.md): search a release source.
- [Indexer feeds](indexer-feeds.md): supply a catalog of indexer definitions.
- [Runtime indexer factories](runtime-indexers.md): create integrations from definitions.
- [Release comparers](release-comparers.md): refine the ordering of eligible releases.
- [Download client integrations](download-clients.md): submit transfers and report their state.
- [Media server integrations](media-servers.md): refresh media libraries and link matching items.
- [Media inspectors](media-inspectors.md): extract technical file properties.

### Running work and report results

- [Jobs](jobs.md): add background work to the existing runtime.
- [Doctor checks](doctor-checks.md): inspect state and report findings.
- [Doctor repairs](doctor-repairs.md): correct a finding after an explicit request.
- [Notification providers](notifications.md): deliver events through a provider.
- [Extension lifecycle](lifecycle.md): respond to extension state changes.

### Adding settings and application features

- [Extension settings](settings.md): declare configuration fields and test their values.
- [UI contributions](ui-contributions.md): add browser controls and views.
- [UI assets](ui-assets.md): supply browser resources.
- [Themes](themes.md): add appearance choices.
- [API endpoints](api-endpoints.md): add HTTP operations.
- [Authentication providers](authentication.md): add an account authentication provider.

Many components have optional capability interfaces. Implement them on the same class as the base contract and register the base contract. Magpie finds the capability through that registration, so registering the optional interface on its own does nothing. Authentication is the exception, and its page shows how to register each flow.

## Replacement components

A replacement component supplies the implementation Magpie uses in place of its default. Register it in the entry point's `RegisterServices`. Magpie registers its defaults after all extensions, and only where no extension has supplied one. A replacement applies throughout the host, not only to the extension's own code.

- [Password policy](password-policy.md): change validation for newly set local passwords.
- [Cache providers](caching.md): replace cache creation and behavior.
- [Key storage](key-storage.md): replace storage for named authentication keys.
- [Stored-value protection](data-protection.md): replace protection for persisted sensitive values.
- [Job retry policy](job-retry-policy.md): replace retry decisions within the existing runtime.
- [Cluster backplanes](cluster-backplane.md): replace the transport for cross-node signals.

If two extensions register the same replacement, the one that loads last wins (see [registration order](#registration-order)). If that extension is disabled or uninstalled, Magpie goes back to its default at the next start, and anything only the replacement could read, such as keys or protected values, becomes unavailable.

## Registration order

Magpie loads enabled extensions in order of their load order number, then by package ID. A lower number loads earlier. Where a guide says the last registration wins, the extension that loads last supplies the implementation.

## Consumption boundary

Magpie supports custom implementations only of the components listed on this page. You can use any other service through its contract, but implementing or replacing another public interface in the [SDK](../../reference/extensions.md#framework-and-abi-compatibility) is unsupported and at your own risk. That includes settings storage and the job runtime: extensions declare settings and consume [`ISettingsService`](../using-built-in-services.md#reading-configuration-and-application-state), and they register jobs and use the existing job services.

For namespaces, entry-point members, and compatibility, see the [extension reference](../../reference/extensions.md). For checks, findings, and repairs, see the [diagnostic reference](../../reference/diagnostics.md).

---

[Development](../README.md) | [Documentation](../../README.md)
