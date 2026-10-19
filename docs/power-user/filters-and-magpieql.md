# Filters and MagpieQL

[Documentation](../README.md) / [Power-user guide](README.md)

A filter selects items without changing them. You build one with the visual controls, or write a [MagpieQL expression](../reference/magpieql.md#values-and-expressions) in **Advanced filter**.

The library filters your [collection](../using/collection.md). **Add content** and [wishlists](../using/monitoring-and-wishlists.md#creating-and-verifying-a-wishlist) send [external metadata searches](#collection-filtering-and-external-discovery) instead.

## Building a collection filter

The examples use four fictional movies.

| Title | [Effective monitoring](../using/monitoring-and-wishlists.md#setting-monitoring) | [Fulfillment](../using/monitoring-and-wishlists.md#understanding-wanted-items-and-upgrades) | Genres |
| --- | --- | --- | --- |
| Harbor Signal | Monitored | Missing | Science Fiction, Drama |
| Orchard Road | Unmonitored | Missing | Drama |
| Quiet Orbit | Shotgun | Upgradable | Science Fiction |
| Winter Room | Monitored | Fulfilled | Drama |

In the movie library, **Simple filter** can select missing fulfillment. The same thing in **Advanced filter** is:

```text
fulfillment = Missing
```

This selects Harbor Signal and Orchard Road. To keep only the ones eligible for automatic acquisition, add monitoring:

```text
monitoring != Unmonitored and fulfillment = Missing
```

Orchard Road drops out because it is unmonitored. To include items that already have files but accept upgrades:

```text
monitoring != Unmonitored and fulfillment in (Missing, Upgradable)
```

Quiet Orbit joins the result, and Winter Room stays out because it is fulfilled. Use `in` for alternative values of one field, because MagpieQL rejects `fulfillment = Missing or fulfillment = Upgradable`. These expressions use [content-state fields](../reference/magpieql.md#content-fields) and [set membership](../reference/magpieql.md#operators).

## Selecting related values

`has` matches an entry in a list such as genres or identifiers.

```text
monitoring != Unmonitored and
fulfillment in (Missing, Upgradable) and
genres has (name = "Science Fiction")
```

This keeps Harbor Signal and Quiet Orbit. For either of two genres, use separate `has` clauses:

```text
genres has (name = "Science Fiction") or genres has (name = "Drama")
```

All conditions inside one `has (...)` must match the same entry. For example, an [identifier source and value](metadata.md#understanding-values-with-several-entries) belong together:

```text
externalIds has (source = "tmdb" and value = "123")
```

For the exact rules, see [collection matching](../reference/magpieql.md#collection-matching). For a classification such as `PG-13`, use [content ratings](../reference/magpieql.md#content-ratings).

## Selecting books by author

In the book library, a stored [author relationship](../reference/magpieql.md#relationship-fields) can be matched like this:

```text
relationships has (kind = "author" and type = "person" and subject = "Frank Herbert")
```

`kind` is the relationship role, `type` is the [kind of the related subject](../using/collection.md#subjects-and-tags), and `subject` matches the person's name. To search an external source, see the [Open Library example](../../src/Magpie.Extensions.OpenLibrary/README.md#search-results).

## Saving and reusing the selection

Saving filters needs [Viewer access](../reference/roles.md) or higher, and [ownership rules](../reference/roles.md#saved-filter-ownership) decide who can edit or delete one. Apply the query, select **Save filter**, and name it, such as `Monitored movies needing media`.

A saved filter stores the criteria, not the results, so it stays current as the collection changes.

Editing a loaded filter changes only the current query until you select **Save changes**. Changes to a shared filter affect everyone who uses it. Edit a filter that **Simple filter** cannot show in **Advanced filter**.

### Reusing criteria with another account

Filters belong to the account that created them. To use the same criteria in another account, copy the expression from **Advanced filter**, paste it there in the same content type view, and **Save filter**. An account with Requester access or higher can instead **Clone** a shared filter.

## Collection filtering and external discovery

Discovery searches external metadata sources for titles to add. It cannot use local data such as monitoring and fulfillment, only criteria the [metadata provider](../administration/metadata.md) supports, such as title, genre, or publication date. Each provider's [extension README](../extensions.md) lists its supported searches.

A valid expression with no results can mean nothing matches or the provider does not support the criteria. The provider warnings say which. The wishlist [preview](../using/monitoring-and-wishlists.md#creating-and-verifying-a-wishlist) shows them.

## Correcting an unexpected result

A rejection names a syntax, field, operator, or value problem. The error "OR expressions are only supported for collection fields on the same collection" means `or` is not supported there, so use `in` for alternative values of one field.

To find out why a known item is missing, reduce the query to one condition and add the others back one at a time. Also check the item's metadata, monitoring, fulfillment, and the content type selected on the page.

A text match on `title` tests every metadata contribution, not only the displayed one. Negative matches also select items with no title. See [missing values](../reference/magpieql.md#missing-values).

---

[Power-user guide](README.md) | [Documentation](../README.md)
