# Installing an extension

[Documentation](../README.md) / [Administration](README.md)

Magpie's features come from [extensions](extensions.md), and the first-party ones come with the Windows installer and the Docker Standard image. When your installation lacks one, you install it from a NuGet feed or from a local `.nupkg` file. This needs [Admin access](access.md#assigning-a-role) and a restart.

For example, a [Bare installation](../installation/docker.md#selecting-an-image) has no movie support until you install `Magpie.Extensions.Movies`.

## Installing during setup

The **Extensions** screen of initial setup has an **Add extension** button. Search for a package and install it, then repeat for any others. If **Restart** appears, select it. Otherwise restart Magpie yourself and select **I have restarted Magpie**. Then select **Continue**.

## Selecting and installing a package

After setup, open **Admin > Extensions** and select **Add**. Search for a package in **Add Extension** and select **Install**.

To install an exact package, enter its **NuGet Package ID**. Leave **Version** empty for the newest stable version compatible with your Magpie release, then select **Install Package**.

Searches use the enabled [**NuGet Feeds**](extensions.md#package-feeds), which by default is nuget.org. A feed that needs credentials must be reachable from the Magpie host itself, not just from your browser.

## Loading and verifying the extension

The extension loads at the next restart. **Admin > System Status** has a **Service Control** panel with **Restart service**. If it shows **Restart unavailable**, restart through your service or container manager. After startup, select **Reload Magpie**.

**Admin > Extensions** then shows the package's version and status. If it shows **Failed**, read the reason and the startup logs. [Doctor](doctor.md) also reports extensions that failed to load.

## Installing a local package

To install a `.nupkg` file that is not in a feed, open **Add Extension**, select **Upload .nupkg**, choose the file, and restart Magpie.

Local upload is unavailable in [`MultiNodeLocalExtensions` mode](clustering.md#selecting-extension-storage). In a cluster, every node needs the package files. See [Clustering](clustering.md#coordinating-extension-changes).

---

[Administration](README.md) | [Documentation](../README.md)
