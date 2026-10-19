# Extensions

[Documentation](../README.md) / [Administration](README.md)

An extension is a package that adds a capability to Magpie. Most of what Magpie does comes from extensions: media types such as movies and TV, metadata sources, indexers, download clients, media servers, and themes. The first-party extensions come with the Windows installer and the Docker Standard image. To add others, see [Installing an extension](install-extensions.md).

Managing extensions requires [Admin access](access.md#assigning-a-role). They are listed on **Admin > Extensions**.

## Settings and connections

An extension can have its own settings, which you edit on its page under **Admin > Extensions**. An extension that adds a kind of service, such as a download client, only supplies that kind. The connection itself, with its address and credentials, is set up with the [other services](storage-and-services.md#service-roles).

## Trust

Extensions run inside the Magpie process, with its permissions and access to its data. Install only packages from publishers you trust.

## Restarts

Installing, updating, enabling, disabling, or reordering an extension takes effect the next time Magpie restarts. Until then, the code that is already loaded keeps running. In a cluster, each node applies the change when it restarts. See [Clustering](clustering.md#coordinating-extension-changes).

## Load order

Magpie loads extensions from the top of the list on **Admin > Extensions** down. The order only matters when two extensions provide the same thing. Extensions bundled with Magpie start at position 0, and packages you install start at 1000. Drag an entry to move it.

## Updates and removal

**Check for Updates** on **Admin > Extensions** finds newer versions that are compatible with your Magpie release. Upgrading Magpie itself keeps your installed extensions as they are.

Disabling an extension keeps its settings. **Uninstall Extension** deletes the package and its settings, including stored credentials, and reinstalling does not bring them back. If you might need them, make a [recovery set](maintenance.md#preparing-a-recovery-set) first.

## Package feeds

Magpie finds packages in NuGet feeds. By default it searches nuget.org for packages tagged `magpie`. You can add other feeds, including private ones that need credentials, on the **NuGet Feeds** tab.

---

[Administration](README.md) | [Documentation](../README.md)
