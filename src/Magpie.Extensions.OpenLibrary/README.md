![Magpie](logo.png)

# Open Library extension for Magpie

[Magpie](https://github.com/magpie-media-platform/magpie) is a self-hosted media manager for any media type, including movies, TV shows, music, and books, with the ability to add more through extensions.

The Open Library extension lets Magpie use Open Library as a [metadata](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/metadata.md) provider for books and authors in the [Books extension](https://github.com/magpie-media-platform/magpie/blob/main/src/Magpie.Extensions.Books/README.md).
Book [discovery](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/filters-and-magpieql.md#collection-filtering-and-external-discovery) returns works, and a work can have several editions with different [identifiers](#identifying-a-work-and-its-edition).

## Configuring

With [Admin access](https://github.com/magpie-media-platform/magpie/blob/main/docs/reference/roles.md), open **Admin > Open Library**, which appears when the [extension](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/extensions.md#settings-and-connections) is enabled and loaded.
Set **Contact** to an email address or phone number that identifies your requests and select **Save Settings**.

## Identifying a work and its edition

The author and work shown in [**Add content**](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/collection.md#adding-a-title) tell similar titles apart. An ISBN identifies an edition, and a search for one returns the edition's work.

A file that matches the work can still be the wrong translation or format, so check language and edition when accepting an [import match](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/files.md#importing-existing-files). Fix a wrong value with a [metadata correction](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/metadata.md#correcting-one-field).

## Search results

Text discovery searches book titles and returns works. Advanced discovery supports criteria such as author, language, publication date, and ISBN.
Author search uses the [MagpieQL relationship fields](https://github.com/magpie-media-platform/magpie/blob/main/docs/reference/magpieql.md#relationship-fields):

```text
relationships has (kind = "author" and type = "person" and subject = "Frank Herbert")
```

Advanced discovery needs at least one criterion that Open Library can search on. For provider settings, see [metadata administration](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/metadata.md).

---

[First-party extensions](https://github.com/magpie-media-platform/magpie/blob/main/docs/extensions.md) | [Documentation](https://github.com/magpie-media-platform/magpie/blob/main/docs/README.md)
