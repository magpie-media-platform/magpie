# Databases

[Documentation](../README.md) / [Administration](README.md)

Magpie supports several database backends, with SQLite as the default, which requires no additional configuration. PostgreSQL and Microsoft SQL Server can be configured with additional setup, and [clustering](clustering.md) needs one of them.

## Configuring PostgreSQL or SQL Server

Initial setup asks for the provider and connection string. They can also be set in the environment:

```text
Magpie__Database__Provider=postgresql
Magpie__Database__ConnectionString=Host=db;Database=magpie;Username=magpie;Password=REPLACE_ME
```

For SQL Server, the provider is `sqlserver` and a connection string looks like `Server=db;Database=magpie;User Id=magpie;Password=REPLACE_ME;Encrypt=True`. The database must exist, and its account must be able to run schema migrations. See the [configuration reference](../reference/configuration.md).

Changing the provider or connection string points Magpie at a different, empty database. Magpie cannot migrate between providers, and a backup restores only into the same provider.

## Preparing management operations

**Admin > Database** lists backups and operations, and says why one is unavailable.

| Provider | Requirements |
| --- | --- |
| SQLite | None. Keep both the backup file and its metadata file. |
| PostgreSQL | `pg_dump` and `pg_restore` of the server's major version or later, in `Magpie:Data:PostgreSqlToolsDirectory` or on `PATH`. A restore needs the database owner role and `CREATEDB`. |
| SQL Server | The server's service account needs file access to its default backup directory. |

The `ghcr.io/magpie-media-platform/magpie:VERSION-postgresql` image includes the PostgreSQL tools. Otherwise [back up PostgreSQL outside Magpie](maintenance.md#backing-up-postgresql-outside-magpie).

Backups, schedules, and restores are in [Maintenance and recovery](maintenance.md).

---

[Administration](README.md) | [Documentation](../README.md)
