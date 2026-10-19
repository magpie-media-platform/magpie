# Metadata providers

[Documentation](../README.md) / [Administration](README.md)

Metadata providers supply names, dates, identifiers, artwork, and relationships. A collection needs a [content extension](extensions.md) together with a provider that supports its content type, such as Movies with TMDb.

Configuring providers requires Admin. Editing one title and inspecting [source contributions](../power-user/metadata.md#inspecting-a-conflicting-value) is covered in [Metadata management](../power-user/metadata.md).

## Selecting providers for the collection

The [first-party extension instructions](../extensions.md) give the requirements and search limits of each provider.

When several providers contribute to an item, Magpie keeps each source's contribution and builds the displayed metadata from them. The most recently fetched value does not always become the displayed one.

## Configuring and verifying a provider

A provider's settings are on its extension page under **Admin > Extensions**. TMDb has its own page at **Admin > TMDb**, where you enter the **API key** and choose a **Language**. If the extension has a configuration test, run it before saving. The [extension instructions](../extensions.md) name the settings page of each other provider.

To check that a provider works, search for a known title in **Add content**, add it, and refresh its metadata. [TMDb](../../src/Magpie.Extensions.Tmdb/README.md) describes its language fallback and limits.

## Applying provider-wide changes

A saved extension setting applies to the provider across the whole instance. After you change credentials, language, enablement, or priority, refresh a representative item and compare its displayed metadata with its source contributions. If the contribution changed but the displayed value did not, a higher-priority source or a manual value is winning. See [Metadata management](../power-user/metadata.md).

The hourly **Content metadata refresh scan** only refreshes items that are still waiting for metadata. To apply a provider change to items that already have metadata, refresh those items.

## Investigating missing or incorrect metadata

| Evidence | Where to look |
| --- | --- |
| Extension is disabled, failed, or awaiting restart | [Extension status](extensions.md) |
| Credential or request error | The provider's settings and the service's own access settings |
| Search works but the title is missing | The title, content type, and provider coverage |
| Correct identifier, unexpected displayed value | Contributions and overrides in [Metadata management](../power-user/metadata.md) |
| Refresh stays queued or fails | The execution in [Admin > System Jobs](jobs-and-automation.md#following-an-execution) |

External sources do not always return everything. The [query guide](../power-user/filters-and-magpieql.md) explains incomplete external results.

---

[Administration](README.md) | [Documentation](../README.md)
