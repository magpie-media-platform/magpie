![Magpie](logo.png)

# Movies extension for Magpie

[Magpie](https://github.com/magpie-media-platform/magpie) is a self-hosted media manager for any media type, including movies, TV shows, music, and books, with the ability to add more through extensions.

The Movies extension adds movies to Magpie as a content type.
It accepts video files and subtitle sidecars (`.srt`, `.ass`, `.vtt`, `.sub`, and `.idx`).

## Preparing movies

Movies need a [metadata provider](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/metadata.md) such as [TMDb](https://github.com/magpie-media-platform/magpie/blob/main/src/Magpie.Extensions.Tmdb/README.md), an [indexer](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/storage-and-services.md#service-roles), a [download client](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/storage-and-services.md#service-roles), and a Movie [root folder](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/storage-and-services.md#adding-a-root-folder) with a suitable [quality tier list](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/storage-and-services.md#quality-choices).
[Storage and services](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/storage-and-services.md#giving-both-processes-the-required-access) covers access to the download and library paths.

## Checking one movie

When you [add a movie](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/collection.md#adding-a-title), its year and [identifiers](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/metadata.md#understanding-values-with-several-entries) show which movie it is.
After a download completes, the movie's **Files** view shows the imported file. Files you already have go through [Library import](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/files.md#importing-existing-files).

---

[First-party extensions](https://github.com/magpie-media-platform/magpie/blob/main/docs/extensions.md) | [Documentation](https://github.com/magpie-media-platform/magpie/blob/main/docs/README.md)
