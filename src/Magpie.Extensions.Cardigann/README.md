![Magpie](logo.png)

# Cardigann extension for Magpie

[Magpie](https://github.com/magpie-media-platform/magpie) is a self-hosted media manager for any media type, including movies, TV shows, music, and books, with the ability to add more through extensions.

The Cardigann extension lets Magpie use Cardigann indexer definitions as [indexers](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/storage-and-services.md#service-roles).
Magpie loads them from a Git feed. By default that is the community definitions from the Jackett repository, and no running Jackett server is needed.

## Configuring definitions and indexers

With [Admin access](https://github.com/magpie-media-platform/magpie/blob/main/docs/reference/roles.md), open **Indexers > Feeds** to see each feed's **Git URL**, **Branch**, and **Definitions path**.
Synchronize the feed, pick an indexer from the **Available** catalog, configure a connection with its settings, and test it. It then shows under **Configured**.

Some definitions need a FlareSolverr service. Set **FlareSolverr URL** in the [extension settings](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/extensions.md#settings-and-connections). In a container, `localhost` is that container.

## Feed updates

Check the [job](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/jobs-and-automation.md#jobs-and-executions) result and the **Available** catalog. Definitions that disappear from a feed are removed together with their configured connections, and so are the definitions of a deleted feed.

Definitions and feeds make requests with the network access of the Magpie host, so enable only the ones you want.

## Verifying search

Run an interactive search for a known title and check each indexer's result or error. An authentication failure points to that connection's credentials, and a missing definition points to feed synchronization.
If nothing is acceptable, check the [quality requirements](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/storage-and-services.md#quality-choices).

---

[First-party extensions](https://github.com/magpie-media-platform/magpie/blob/main/docs/extensions.md) | [Documentation](https://github.com/magpie-media-platform/magpie/blob/main/docs/README.md)
