![Magpie](logo.png)

# TV extension for Magpie

[Magpie](https://github.com/magpie-media-platform/magpie) is a self-hosted media manager for any media type, including movies, TV shows, music, and books, with the ability to add more through extensions.

The TV extension adds TV shows to Magpie as a content type, as a [hierarchy](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/collection.md#content-types-and-hierarchy) of shows, seasons, and episodes.
Episodes accept video files and subtitle sidecars (`.srt`, `.ass`, `.vtt`, `.sub`, and `.idx`), and [file matching](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/files.md#importing-existing-files) needs [metadata](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/metadata.md) with the correct show and episode numbers.

## Preparing a show

TV needs a metadata provider, [TVmaze](https://github.com/magpie-media-platform/magpie/blob/main/src/Magpie.Extensions.Tvmaze/README.md) or [TMDb](https://github.com/magpie-media-platform/magpie/blob/main/src/Magpie.Extensions.Tmdb/README.md), plus a TV [root folder](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/storage-and-services.md#adding-a-root-folder), an [indexer](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/storage-and-services.md#service-roles), and a download client.
Season and episode numbers come from the metadata provider and can differ from how releases are numbered.

## Selecting the acquisition scope

Searching on one episode shows releases for that episode. At season or show level a release can cover several children. The children's inherited [monitoring](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/monitoring-and-wishlists.md#setting-monitoring) determines what a broad selection acquires.

A pack download can complete while some episodes are not imported, so after [import](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/search-and-download.md#following-completion) check the **Files** view of each episode.
For wrong numbers or missing child metadata, see [metadata management](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/metadata.md#inspecting-a-conflicting-value). For a release that does not match, see [Searching and download](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/search-and-download.md#interpreting-an-unsuccessful-search).

---

[First-party extensions](https://github.com/magpie-media-platform/magpie/blob/main/docs/extensions.md) | [Documentation](https://github.com/magpie-media-platform/magpie/blob/main/docs/README.md)
