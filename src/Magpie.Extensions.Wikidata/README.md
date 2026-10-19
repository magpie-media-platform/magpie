![Magpie](logo.png)

# Wikidata extension for Magpie

[Magpie](https://github.com/magpie-media-platform/magpie) is a self-hosted media manager for any media type, including movies, TV shows, music, and books, with the ability to add more through extensions.

The Wikidata extension lets Magpie use Wikidata as a [metadata](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/metadata.md) provider for movies and television, including shows, seasons, and episodes, in the [Movies](https://github.com/magpie-media-platform/magpie/blob/main/src/Magpie.Extensions.Movies/README.md) and [TV](https://github.com/magpie-media-platform/magpie/blob/main/src/Magpie.Extensions.Tv/README.md) content types.

## Configuring

Wikidata needs no API key.
With [Admin access](https://github.com/magpie-media-platform/magpie/blob/main/docs/reference/roles.md), open **Admin > Wikidata**, which appears when the [extension](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/extensions.md#settings-and-connections) is enabled and loaded. Set **Contact** to the contact information for service requests, set **Language** to a Wikidata language code such as `en`, and select **Save Settings**.
The matching content extension and a compatible [root folder](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/storage-and-services.md#adding-a-root-folder) are needed before adding titles.

## Checking identity and contributions

When you [add a title](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/collection.md#adding-a-title), its name, [content type](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/collection.md#content-types-and-hierarchy), and publication information tell titles apart. The Wikidata [identifier](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/metadata.md#understanding-values-with-several-entries) and its [contribution in the metadata inspector](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/metadata.md#inspecting-a-conflicting-value) show what it supplied.

With several providers, the displayed result can mix sources. Fix a wrong value with a [field correction](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/metadata.md#correcting-one-field) or a change of contribution order.

## Search results

[Discovery](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/filters-and-magpieql.md#collection-filtering-and-external-discovery) searches movies and shows, not seasons or episodes. Queries can use titles, identifiers, and criteria such as genre, language, publication date, and relationships.
Advanced discovery needs a criterion that selects candidates, not only exclusions or presence checks. Provider warnings report service limits, so an empty result does not prove nothing matches. For provider settings, see [metadata administration](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/metadata.md).

---

[First-party extensions](https://github.com/magpie-media-platform/magpie/blob/main/docs/extensions.md) | [Documentation](https://github.com/magpie-media-platform/magpie/blob/main/docs/README.md)
