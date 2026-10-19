![Magpie](logo.png)

# MusicBrainz extension for Magpie

[Magpie](https://github.com/magpie-media-platform/magpie) is a self-hosted media manager for any media type, including movies, TV shows, music, and books, with the ability to add more through extensions.

The MusicBrainz extension lets Magpie use MusicBrainz as a [metadata](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/metadata.md) provider for artists, albums, and tracks in the [Music extension](https://github.com/magpie-media-platform/magpie/blob/main/src/Magpie.Extensions.Music/README.md).

## Configuring

With [Admin access](https://github.com/magpie-media-platform/magpie/blob/main/docs/reference/roles.md), open **Admin > MusicBrainz**, which appears when the [extension](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/extensions.md#settings-and-connections) is enabled and loaded.
Set **Contact** to identify your requests to the service and select **Save Settings**.

MusicBrainz limits requests per IP address. If several Magpie nodes share an outgoing IP address, run MusicBrainz work from only one of them.

## Matching an album before importing files

An album result is a MusicBrainz release group, which can hold several editions with different track lists. A track result is a recording. Check the [identifiers](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/metadata.md#understanding-values-with-several-entries) and each track, not only the album title.

After [import](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/files.md#importing-existing-files), the [associated files](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/files.md#inspecting-and-correcting-file-associations) show the result.

## Search results

[Discovery](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/filters-and-magpieql.md#collection-filtering-and-external-discovery) searches artists, albums, and tracks, so select the level of the collection item you want.
Advanced discovery needs a criterion such as a title or an identifier. Provider warnings report limits and failures. For provider settings, see [metadata administration](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/metadata.md).

---

[First-party extensions](https://github.com/magpie-media-platform/magpie/blob/main/docs/extensions.md) | [Documentation](https://github.com/magpie-media-platform/magpie/blob/main/docs/README.md)
