# Maintenance and recovery

[Documentation](../README.md) / [Administration](README.md)

A database backup alone does not make a working recovery. It also needs the matching keys, configuration, extensions, and files.

## Preparing a recovery set

Keep these together, recorded with the Magpie version, extension versions, database provider, and backup time:

- The database backup and its metadata file.
- Host and deployment configuration, and any secret values the environment supplies.
- The extension directory and the application data directory, which holds the keys.
- Media and download files. The database does not contain them.

Keep copies outside the disk or volume they protect. For a consistent copy, stop every Magpie process that writes to the installation, including each [cluster node](clustering.md). Never copy an active SQLite file; use the Magpie backup.

### Preserving keys

The `keys` directory in the data directory holds `integration-secret.key` (decrypts stored integration secrets), `api-key-hmac.key` (validates [API keys](access.md#issuing-an-api-key)), and `jwt-hmac.key` (validates sign-in tokens). A missing key file is replaced with a new one, so stored credentials must be entered again and API keys and sessions become invalid.

`MAGPIE_SECRET_KEY`, when set, is a Base64-encoded 32-byte key used in place of `integration-secret.key`. Keep its exact value in the recovery set.

## Creating and retaining a database backup

On **Admin > Database**, **Create Backup** queues a backup job. When it finishes in **Admin > System Jobs**, copy the backup file and its metadata off the original storage. Provider requirements are in [Preparing management operations](databases.md#preparing-management-operations).

To schedule backups, edit the **Database maintenance schedule** under **Admin > System Jobs > Scheduled Jobs** and set `backupEnabled` to `true`. `backupIntervalDays` defaults to 1 and `backupRetentionCount` to 7. Older scheduled backups are deleted, manual ones never are, and none are copied off the host.

### Backing up PostgreSQL outside Magpie

PostgreSQL's own tools also work, but those backups do not appear on the **Database** page. Use the custom archive format ([pg_dump](https://www.postgresql.org/docs/current/app-pgdump.html)). A dump excludes global roles, keys, configuration, extensions, and media.

## Rehearsing recovery

Test on an isolated copy with its own database and storage before replacing a working installation. Scheduled work starts as soon as the host does, so first make sure the copy cannot send downloads, [notifications](notifications.md), or media-server updates to production services.

Restore the recorded versions, keys, secrets, and files, start the copy, and check:

1. [Readiness](observability.md#health-and-readiness) and the startup logs.
2. Sign-in with an account from the backup.
3. Extension status, [root folders](storage-and-services.md#adding-a-root-folder), and [remote paths](storage-and-services.md#remote-paths).
4. [Doctor findings](doctor.md) and representative records and files.

An empty collection or the setup page means the restore did not work. Do not create an administrator.

## Restoring through Magpie

A restore replaces the current database with an earlier state, so save a current recovery set first and stop other processes that write to the database.

On **Admin > Database**, select **Restore** on the backup and confirm. The restore applies at the next startup, and the log then shows "Applied pending database restore before migration". Then run the checks above and [reconcile external state](#reconciling-external-state).

## Restoring an external database

1. Stop all Magpie nodes and other writers to the database.
2. Isolate the host as in [Rehearsing recovery](#rehearsing-recovery), and prepare the keys, configuration, extensions, and files.
3. Restore with [pg_restore](https://www.postgresql.org/docs/current/app-pgrestore.html) or [SQL Server tools](https://learn.microsoft.com/en-us/sql/relational-databases/backup-restore/restore-a-database-backup-using-ssms?view=sql-server-ver17).
4. Start the host, run the checks, and reconcile external state.

## Restoring SQLite without the original database or interface

A completed SQLite backup is a `.db` file plus a matching `.db.metadata.json` file, in `backups` in the data directory by default.

1. Stop Magpie, leaving the original data untouched.
2. Build a new data directory, here `/srv/magpie-recovery/data`, from the recovery set's `config`, `keys`, and `extensions`.
3. Copy the completed backup to `/srv/magpie-recovery/data/magpie.db`. Never use a `.tmp` file, the damaged database, its `-wal`, `-shm`, or `-journal` files, `restore-pending.json`, or `restore-staging`.
4. Put copies of the media and downloads in `/srv/magpie-recovery/media`.
5. Isolate the host. Binding the port to `127.0.0.1` does not stop outgoing downloads or notifications.
6. Start the container, with `VERSION` set to the recorded version and the original secret environment values supplied:

```sh
docker run -d --name magpie-recovery --restart no \
  -p 127.0.0.1:8081:8080 \
  --mount type=bind,source=/srv/magpie-recovery/data,target=/app/data \
  --mount type=bind,source=/srv/magpie-recovery/media,target=/media,readonly \
  -e Magpie__Database__Provider=sqlite \
  -e 'Magpie__Database__ConnectionString=Data Source=/app/data/magpie.db;Mode=ReadWrite' \
  ghcr.io/magpie-media-platform/magpie:VERSION
```

7. Read the container log, open `http://localhost:8081`, and run the checks. If the setup page appears or records are missing, stop.

For a native installation, stop the service, move the damaged database, its `-wal`, `-shm`, and `-journal` files, `restore-pending.json`, and `restore-staging` out of the data directory, copy the backup to `data/magpie.db`, restore `config`, `keys`, `extensions`, and secrets, isolate the host, and start.

## Reconciling external state

Recovery does not undo what a download client already finished, and does not bring back files deleted after the backup. Compare active download records with the client, and check an earlier operation's result before repeating it. See [completed-download diagnosis](troubleshooting.md#download-completes-but-import-does-not).

## Upgrading and rolling back

Before an upgrade, have a [recovery set](#preparing-a-recovery-set). In a cluster, upgrade every node, because a node older than the database does not run jobs.

### Upgrading the installation

See the [Docker](../installation/docker.md#replacing-the-container-with-a-selected-version) and Helm [upgrade order](../installation/helm.md#upgrade-order) guides. For [Windows](../installation/windows.md), [standalone](../installation/standalone.md), or [Kubernetes](../installation/kubernetes.md), stop the host, replace the application files or image while keeping persistent directories, then start. Extensions are updated separately, through [extension management](extensions.md#updates-and-removal).

### Verifying the upgrade

Check the startup logs for errors. If the setup page appears, stop and verify the database and persistent paths. Then sign in, check the collection and extensions, run [Doctor](doctor.md), and test the service connections.

### Rolling back the installation

After an upgrade has changed the database or settings, an older application version alone is not a rollback. Restore the matching database and persistent state from the recovery set.

---

[Administration](README.md) | [Documentation](../README.md)
