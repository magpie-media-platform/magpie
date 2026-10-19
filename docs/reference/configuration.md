# Host configuration reference

[Documentation](../README.md) / [Reference](README.md)

This page lists the [host settings](../administration/configuration.md#changing-host-settings) Magpie reads at startup, with their defaults. Magpie loads `appsettings.json` from the content root, then `config/appsettings.json`, then environment variables, and a later source overrides an earlier one. Initial setup writes the database selection to `config/appsettings.json`, and for a cluster also the cluster mode, node roles, and job types. Restart Magpie after a change.

Host settings configure the process and its storage. [Instance settings](../administration/configuration.md#changing-instance-settings), such as [service connections](../administration/storage-and-services.md#service-roles), [extension settings](../administration/extensions.md#settings-and-connections), and [theme defaults](../administration/configuration.md#theme-defaults), live in the database and are managed in the interface.

For an environment variable, replace each colon with two underscores. For example, `Magpie:Data:Path` becomes `Magpie__Data__Path`. List entries use numbered suffixes, such as `Magpie__KnownProxies__0`.

**On this page**

- [Application and storage](#application-and-storage)
- [Authentication](#authentication)
- [Background work](#background-work)
- [Other environment settings](#other-environment-settings)
- [Extension environment settings](#extension-environment-settings)

## Application and storage

| Key | Default | Meaning |
| --- | --- | --- |
| `Magpie:Database:Provider` | `sqlite` | [Database choice](../administration/databases.md): `sqlite`, `postgresql` (alias `postgres`), or `sqlserver` (alias `mssql`). |
| `Magpie:Database:ConnectionString` | `magpie.db` in the data directory | Required for PostgreSQL and SQL Server. |
| `Magpie:Data:Path` | `data` under the content root | Mutable application data. |
| `Magpie:Data:PostgreSqlToolsDirectory` | Unset | Directory that Magpie searches for `pg_dump` and `pg_restore` before `PATH`. |
| `Magpie:Extensions:Path` | `extensions` under the content root | Installed extension packages. |
| `Magpie:Cluster:Mode` | `SingleNode` | [Cluster mode](../administration/clustering.md#selecting-extension-storage). Also accepts `MultiNodeSharedExtensions` and `MultiNodeLocalExtensions`. |
| `Magpie:Cluster:NodeId` | Machine name | Unique node identity in a cluster. |
| `Magpie:Cluster:Roles` | Empty list: all roles | Roles that this node serves. `Ui` serves the web interface. `Api` serves the API. Without it, the node serves only the [health endpoints](telemetry.md#health-endpoints). `JobRunner` runs jobs. |
| `Magpie:KnownProxies` | Empty list | Proxy addresses or CIDR ranges whose `X-Forwarded-For` and `X-Forwarded-Proto` headers Magpie accepts. When the list is empty, Magpie ignores forwarded headers. An invalid entry stops startup. |
| `Magpie:AllowedOrigins` | Empty list | Browser origins that can call the API from another origin. |
| `Http:Timeout` | `00:01:00` | Total time for an outgoing HTTP request, including retries and upstream cooldown waits. The value must be greater than zero. |

The content root is the working directory of the Magpie process. For a Windows service, it is the installation directory. Docker uses `/app` and sets extension storage to `/app/data/extensions`.

Changing `Magpie:Data:Path`, the database provider, or the connection string points Magpie at a new location or database. It does not move or copy the previous database or key files, so transfer them first. See [Maintenance](../administration/maintenance.md).

In a cluster, each node ID must be unique and stable. See [Clustering](../administration/clustering.md).

## Authentication

These keys control local accounts and the tokens Magpie issues. Duration values use `[d.]hh:mm:ss`.

| Key | Default | Meaning |
| --- | --- | --- |
| `Magpie:Auth:LockoutOptions:Enabled` | `true` | Locks a local account after repeated failed sign-in attempts. |
| `Magpie:Auth:LockoutOptions:MaxFailedAttempts` | `5` | Number of consecutive failed attempts that locks the account. |
| `Magpie:Auth:LockoutOptions:LockoutDuration` | `00:15:00` | Time that the account stays locked. |
| `Magpie:Auth:AccessTokenLifetime` | `00:15:00` | Time that an issued access token stays valid. |
| `Magpie:Auth:RefreshTokenLifetime` | `7.00:00:00` | Time that an issued refresh token stays valid. |
| `Magpie:Auth:Issuer` | `magpie` | Issuer claim in issued tokens. |
| `Magpie:Auth:Audience` | `magpie` | Audience claim in issued tokens. |

To remove a lock early, see [Unlocking an account](../administration/access.md#unlocking-an-account).

## Background work

Duration values use `[d.]hh:mm:ss`.

| Key | Default | Meaning |
| --- | --- | --- |
| `Jobs:PoolSize` | `50` | [Job](../administration/jobs-and-automation.md#jobs-and-executions) worker count. Magpie raises a value below 5 to 5. The value `0` does not stop jobs. |
| `Jobs:JobTypes` | Empty list: all job types | Job types that this node runs. |
| `Jobs:ConcurrencyLimits` | Empty | Maximum number of concurrent executions, keyed by job type. It applies only to a job type whose specification declares a concurrency limit. No built-in job declares one. Magpie rejects a value below 1. |
| `Jobs:MlfqQuanta` | Four unset entries | Timing overrides for each [job scheduler level](#scheduler-levels). |
| `Jobs:RunningStaleCutoff` | `00:03:00` | Heartbeat age at which a running execution becomes stale. |
| `Jobs:StaleJobCleanupInterval` | `01:00:00` | Interval between cleanups of stale executions. |
| `Jobs:ClusteredStaleJobCleanupInterval` | `00:00:30` | Maximum cleanup interval in a cluster. |
| `Jobs:ProgressFlushTimeout` | `00:00:30` | Maximum wait for a progress write. |
| `Search:CoalescingWindow` | `00:15:00` | Period in which a repeated search uses the result of the previous search. |
| `Search:MinimumSearchAge` | `1.00:00:00` | Minimum age of a search result before a scan searches the same item again. |

### Scheduler levels

The job scheduler puts running executions into four levels. When an execution runs longer than the time for its level, it moves to the next level if that level has capacity. These times are not execution timeouts and do not cancel jobs.

`Jobs:MlfqQuanta` sets the time for each level. Unset entries use 10 seconds, 60 seconds, 5 minutes, and no time limit for levels 0 through 3. For an environment override, use a numbered key, such as `Jobs__MlfqQuanta__0=00:00:20`.

## Other environment settings

| Name | Purpose |
| --- | --- |
| `ASPNETCORE_URLS` | HTTP listener addresses. The distribution examples use port 8080. |
| `MAGPIE_SECRET_KEY` | Optional key that protects stored integration credentials. The value is Base64 and decodes to exactly 32 bytes. |
| `MAGPIE_API_KEY` | [API key](../administration/access.md#issuing-an-api-key) for [remote CLI commands](../administration/api-and-cli.md#targeting-a-running-instance). |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Enables export to an [OTLP destination](../administration/observability.md#telemetry-signals). |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | Export transport, such as `grpc`. |

`Logging:LogLevel` sets the host log filter. See [Adjusting detail for an investigation](../administration/observability.md#adjusting-detail-for-an-investigation).

`MAGPIE_SECRET_KEY` is not an API key, and it does not replace the authentication key files. Without it, Magpie creates and uses `keys/integration-secret.key` in the data directory. When the variable is set, Magpie protects new values with it and still reads that key file, if it exists, to decrypt older values. See [Preserving keys](../administration/maintenance.md#preserving-keys).

## Extension environment settings

First-party [extensions](../administration/extensions.md) read these variables.

| Name | Extension | Purpose |
| --- | --- | --- |
| `MAGPIE_TMDB_API_KEY` | TMDb | API key. A saved extension setting takes precedence. |
| `MAGPIE_TMDB_LANGUAGE` | TMDb | Metadata language. A saved extension setting takes precedence. |
| `MAGPIE_MUSICBRAINZ_CONTACT` | MusicBrainz | Contact text in the request User-Agent. This variable takes precedence over the saved extension setting. |
| `MAGPIE_OPENLIBRARY_CONTACT` | Open Library | Contact text in the request User-Agent. A saved extension setting takes precedence. |
| `MAGPIE_TVMAZE_CONTACT` | TVmaze | Contact text in the request User-Agent. A saved extension setting takes precedence. |
| `MAGPIE_WIKIDATA_CONTACT` | Wikidata | Contact text in the request User-Agent. A saved extension setting takes precedence. |
| `MAGPIE_WIKIDATA_LANGUAGE` | Wikidata | Metadata language. A saved extension setting takes precedence. |

---

[Reference](README.md) | [Documentation](../README.md)
