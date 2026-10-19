# First-party extensions

[Documentation](README.md)

Most of what Magpie does comes from [extensions](administration/extensions.md). Each first-party extension has a README covering what it adds and how to set it up. To add one, see [Installing an extension](administration/install-extensions.md).

## Content types

A content type defines how a kind of media is organized and which files it accepts. See [Movies](../src/Magpie.Extensions.Movies/README.md), [TV](../src/Magpie.Extensions.Tv/README.md), [Music](../src/Magpie.Extensions.Music/README.md), and [Books](../src/Magpie.Extensions.Books/README.md).

## Metadata

[Metadata providers](administration/metadata.md) supply titles, descriptions, and images, and power [Add content](using/collection.md#adding-a-title) and wishlists. See [TMDb](../src/Magpie.Extensions.Tmdb/README.md), [TVmaze](../src/Magpie.Extensions.Tvmaze/README.md), [MusicBrainz](../src/Magpie.Extensions.MusicBrainz/README.md), [Open Library](../src/Magpie.Extensions.OpenLibrary/README.md), and [Wikidata](../src/Magpie.Extensions.Wikidata/README.md).

## Connecting services and inspecting files

These connect Magpie to [services](administration/storage-and-services.md#service-roles). See [Cardigann](../src/Magpie.Extensions.Cardigann/README.md) for indexers, [qBittorrent](../src/Magpie.Extensions.Qbittorrent/README.md) for downloads, [Jellyfin](../src/Magpie.Extensions.Jellyfin/README.md) for a media server, and [MediaInfo (FFprobe)](../src/Magpie.Extensions.MediaInfo.FFProbe/README.md) for reading file details.

To write your own, see the [developer guide](development/README.md#extensions).

---

[Documentation](README.md) | [Magpie overview](../README.md)
