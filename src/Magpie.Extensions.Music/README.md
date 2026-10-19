![Magpie](logo.png)

# Music extension for Magpie

[Magpie](https://github.com/magpie-media-platform/magpie) is a self-hosted media manager for any media type, including movies, TV shows, music, and books, with the ability to add more through extensions.

The Music extension adds music to Magpie as a content type, organized as a [hierarchy](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/collection.md#content-types-and-hierarchy) of artists, albums, and tracks:

```text
Artist
  Album
    Track
```

Magpie acquires albums and tracks, but not artists.
Audio files can be [associated with albums or tracks](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/files.md), and cue (`.cue`), log (`.log`), and playlist (`.m3u`) sidecars are accepted with them.

## Configuring metadata and storage

Music needs a compatible [metadata provider](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/metadata.md), such as [MusicBrainz](https://github.com/magpie-media-platform/magpie/blob/main/src/Magpie.Extensions.MusicBrainz/README.md), a Music [root folder](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/storage-and-services.md#adding-a-root-folder), and a [quality tier list](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/storage-and-services.md#quality-choices) for the audio you want.
You also need [indexers and download clients](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/storage-and-services.md#connecting-a-service).

An album title alone does not identify every edition. See the MusicBrainz [identity guidance](https://github.com/magpie-media-platform/magpie/blob/main/src/Magpie.Extensions.MusicBrainz/README.md#matching-an-album-before-importing-files).

## Checking an album

When you [add an album](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/collection.md#adding-a-title), its artist and tracks show whether it matches your files or [release](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/search-and-download.md). Fix an identity mismatch before accepting a file association.
An existing album directory goes through [Library import](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/files.md#importing-existing-files), and acquisition uses **Interactive Search** on the album or track.

An album is complete only if every track has a file. See [metadata management](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/metadata.md#inspecting-a-conflicting-value) for corrections and [monitoring](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/monitoring-and-wishlists.md#setting-monitoring) for acquisition choices.

---

[First-party extensions](https://github.com/magpie-media-platform/magpie/blob/main/docs/extensions.md) | [Documentation](https://github.com/magpie-media-platform/magpie/blob/main/docs/README.md)
