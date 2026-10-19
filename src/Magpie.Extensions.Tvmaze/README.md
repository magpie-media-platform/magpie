![Magpie](logo.png)

# TVmaze extension for Magpie

[Magpie](https://github.com/magpie-media-platform/magpie) is a self-hosted media manager for any media type, including movies, TV shows, music, and books, with the ability to add more through extensions.

The TVmaze extension lets Magpie use TVmaze as a [metadata](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/metadata.md) provider for shows in the [TV extension](https://github.com/magpie-media-platform/magpie/blob/main/src/Magpie.Extensions.Tv/README.md).

## Configuring

With [Admin access](https://github.com/magpie-media-platform/magpie/blob/main/docs/reference/roles.md), open **Admin > TVmaze**, which appears when the [extension](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/extensions.md#settings-and-connections) is enabled and loaded.
Set **Contact** to the contact information that identifies your requests to TVmaze and select **Save Settings**. This is not a sign-in credential.
The TV extension and a compatible [root folder](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/storage-and-services.md#adding-a-root-folder) are needed before adding shows.

## Checking a show's episode structure

Before [adding a show](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/collection.md#adding-a-title), check its episodes for a season. If releases use different numbering, fix a wrong association with [file correction](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/files.md#inspecting-and-correcting-file-associations).

When another provider also contributes, the TVmaze [contribution](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/metadata.md#inspecting-a-conflicting-value) can differ from the resolved value.

## Search results

Text [discovery](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/filters-and-magpieql.md#collection-filtering-and-external-discovery) searches show titles, not seasons or episodes. Advanced discovery also supports show identifiers and person relationships.
If TVmaze cannot supply data a criterion needs, the provider leaves out those candidates and reports a warning. For provider settings, see [metadata administration](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/metadata.md).

---

[First-party extensions](https://github.com/magpie-media-platform/magpie/blob/main/docs/extensions.md) | [Documentation](https://github.com/magpie-media-platform/magpie/blob/main/docs/README.md)
