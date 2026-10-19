# Storage and services

[Documentation](../README.md) / [Administration](README.md)

Magpie works with the paths it sees, which can differ from the host's. In the [Docker example](../installation/docker.md), host `/srv/media` is mounted at `/media`, so `/srv/media/movies` is `/media/movies` to Magpie.

## Service roles

| Service | Role |
| --- | --- |
| Metadata provider | Supplies descriptions, dates, identifiers, and images. |
| Indexer | Searches a release source for candidate downloads. |
| Download client | Transfers the selected release and reports the completed file path. |
| Media server | Plays library files, and can refresh its library after an import. |
| Media inspector | Reads technical file details such as codecs and duration. |

An [extension](extensions.md) supplies the code for a service, and you add the address and credentials as a connection.

## Adding a root folder

A root folder is an import destination for one [content type](../using/collection.md#content-types-and-hierarchy) and carries that type's defaults. On **Admin > Root Folders**, select **Add Folder**. The Magpie process account needs write access to the directory, which only an imported file proves.

The defaults are **Default Quality Tiers** (see [Quality choices](#quality-choices)), **Default monitoring** (or **Use system default**, the [installation-wide choice](configuration.md#setting-the-system-monitoring-default)), and **Upgrade File Handling**, where **Replace** deletes the lower-quality file after an upgrade import and **Keep both** keeps it.

The first folder of a content type becomes its default, and a title can pick another through its **Root Folder** control. To import existing files, see [Files](../using/files.md).

## Configuring file naming

A rename mask builds file names from [metadata](../power-user/metadata.md) and file properties. It applies to new downloads and requested renames, not to existing files.

Edit the root folder, enable **Configure a file rename mask**, and fill in **Rename template**. The result is relative to the content folder, and `/` creates a subfolder. For example, `{Title} ({Year}).{Ext}` renames `movie.mkv` to `Example Movie (2024).mkv`. **Available tokens** lists every token, and `:upper`, `:lower`, or `:00` change case or padding.

### Checking token values and previewing failures

In the rename dialog, **Token values for** shows a file's values. A missing value or unknown token fails an explicit rename of that file, as the example mask does when the movie has no year. Download placement instead leaves it empty, so an imported path can have empty parts.

Check each **Destination path** and **Outcome** before **Apply valid renames**, which processes the valid files even if others fail.

## Giving both processes the required access

A torrent import copies the file so the client can keep seeding, so allow space for both copies. Other protocols move the file and delete the source, which needs delete permission there.

A Windows service lacks the desktop user's permissions, and a directory your shell can open may not be mounted into a container. See the [Docker](../installation/docker.md), [Windows](../installation/windows.md), and [Kubernetes](../installation/kubernetes.md) guides.

## Connecting a service

Install the extension first ([Installing an extension](install-extensions.md)). Metadata providers are configured in their extension settings. Indexers, download clients, and media servers are added as connections with an address and credentials, each with a **Test** action. For containers on a shared network, use the service's network name, because `localhost` inside the Magpie container is the container itself.

A passing test does not confirm the whole path. Find a known title for a metadata provider, run an [interactive search](../using/search-and-download.md#selecting-a-release) for an indexer, follow one download through import for a download client, and check the library refresh for a media server.

A failed test makes a connection Degraded, and an authentication, not-found, or fatal error or 3 failures in a row make it Unhealthy. Magpie does not use an Unhealthy connection until a test succeeds. For indexer definitions, see [Cardigann](../../src/Magpie.Extensions.Cardigann/README.md).

## Remote paths

When the download client and Magpie see the same storage under different paths, a remote path mapping translates between them. For example, a client at `qbittorrent` reports `/downloads/complete/Example.Movie.2024/movie.mkv`, and Magpie sees it under `/media/downloads`. On **Admin > Remote Path Mappings**, **Add Mapping** takes:

| Field | Example |
| --- | --- |
| Host | `qbittorrent` (the download client's host name) |
| Remote Path | `/downloads/complete` |
| Local Path | `/media/downloads` |

**Test Path** checks only that the local directory is readable. If the file is still absent, see [completed-download diagnosis](troubleshooting.md#download-completes-but-import-does-not).

## Quality choices

A quality tier describes a kind of release, such as `1080p WEB-DL`. A tier list orders tiers and can apply exclusions. Root folders supply the default list, and an item or its ancestor can pick another. **Admin > Quality Tiers** shows the order, cutoff markers, upgrade setting, and exclusions.

When several tiers match, the first in list order applies. A quality match alone never causes a grab: the release must also match the title, avoid exclusions, and be monitored.

Changing a shared list or a tier applies at once to everything that uses it. To see the effect, run an **Interactive Search** before and after. A policy change never converts, renames, or deletes existing files.

---

[Administration](README.md) | [Documentation](../README.md)
