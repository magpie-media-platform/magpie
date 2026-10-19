![Magpie](logo.png)

# TMDb extension for Magpie

[Magpie](https://github.com/magpie-media-platform/magpie) is a self-hosted media manager for any media type, including movies, TV shows, music, and books, with the ability to add more through extensions.

The TMDb extension lets Magpie use TMDb (The Movie Database) as a [metadata](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/metadata.md) provider for movies, shows, and people in the [Movies](https://github.com/magpie-media-platform/magpie/blob/main/src/Magpie.Extensions.Movies/README.md) and [TV](https://github.com/magpie-media-platform/magpie/blob/main/src/Magpie.Extensions.Tv/README.md) content types.

## Configuring

The TMDb extension requires a TMDb API key (see the [TMDb API instructions](https://developer.themoviedb.org/docs/getting-started)).
With [Admin access](https://github.com/magpie-media-platform/magpie/blob/main/docs/reference/roles.md), open **Admin > TMDb**, which appears when the [extension](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/extensions.md#settings-and-connections) is enabled. Enter the **API key**, set **Language** to a locale such as `en-US`, and select **Save Settings**.

A saved value wins over the environment variable, and the language defaults to `en-US`.

| Setting | Environment variable |
| --- | --- |
| **API key** | `MAGPIE_TMDB_API_KEY` |
| **Language** | `MAGPIE_TMDB_LANGUAGE` |

The matching content extension must be enabled, and adding titles requires a compatible [root folder](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/storage-and-services.md#adding-a-root-folder).

## Verifying title identity and metadata

When you [add a title](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/collection.md#adding-a-title), the title, year, and media type tell a remake from another work with the same name.
The [metadata inspector](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/metadata.md#inspecting-a-conflicting-value) shows the TMDb contribution and its [identifiers](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/metadata.md#understanding-values-with-several-entries). If a refreshed value is not the displayed one, the contribution order or a manual correction explains it.

## Search results

[Discovery](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/filters-and-magpieql.md#collection-filtering-and-external-discovery) searches movies and shows by title or by supported criteria, including genre, language, and publication date. Discovery does not search seasons or episodes.
TMDb limits each query to 500 pages, so use narrower criteria if you get an incomplete-result warning. For provider settings, see [metadata administration](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/metadata.md).

---

[First-party extensions](https://github.com/magpie-media-platform/magpie/blob/main/docs/extensions.md) | [Documentation](https://github.com/magpie-media-platform/magpie/blob/main/docs/README.md)
