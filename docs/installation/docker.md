# Docker

[Documentation](../README.md) / [Installation](README.md)

Running Magpie in Docker takes one command, followed by [setup](../quick-start.md#completing-setup) at [localhost:8080](http://localhost:8080) and the [first download](first-download.md).

## Selecting an image

Images are at `ghcr.io/magpie-media-platform/magpie` for Linux on AMD64 and ARM64.

| Image tag | Contents |
| --- | --- |
| `latest`, `VERSION` | Standard: the application with the [first-party extensions](../extensions.md) |
| `VERSION-bare` | Bare: the application without extensions, which you install during setup |
| `VERSION-postgresql` | Standard, plus the PostgreSQL 18 tools `pg_dump` and `pg_restore` |

`VERSION` has no leading `v`.

## Preserving data and connecting storage

```sh
docker run -d --name magpie --restart unless-stopped \
  -p 127.0.0.1:8080:8080 \
  -v magpie-data:/app/data \
  --mount type=bind,source=/srv/media,target=/media \
  ghcr.io/magpie-media-platform/magpie:latest
```

`/app/data` holds the database, settings, keys, and installed extensions, so it needs a named volume or a writable host directory. The `--mount` line makes the host folder `/srv/media` appear as `/media`, the path entered as the [root folder](../administration/storage-and-services.md#adding-a-root-folder).

Adding `-e PUID=<uid> -e PGID=<gid>` runs Magpie as another account, such as the owner of the media.

## Connecting to Magpie and other services

The command above publishes port `8080` only on the Docker host. Using `-p 8080:8080` makes it reachable from other computers, after the administrator account exists. See [Access](../administration/access.md) for HTTPS and reverse proxies.

Inside the container, `localhost` means Magpie itself. A service in another container, such as qBittorrent, needs a shared Docker network, and Magpie reaches it by container name:

```sh
docker network connect media-services magpie
```

## Replacing the container with a selected version

After preparing a [recovery set](../administration/maintenance.md#preparing-a-recovery-set), recreate the container with the same options:

```sh
docker pull ghcr.io/magpie-media-platform/magpie:VERSION
docker stop magpie
docker rm magpie
docker run -d --name magpie --restart unless-stopped \
  -p 127.0.0.1:8080:8080 \
  -v magpie-data:/app/data \
  ghcr.io/magpie-media-platform/magpie:VERSION
```

[Upgrade verification](../administration/maintenance.md#verifying-the-upgrade) follows.

---

[Installation](README.md) | [Documentation](../README.md)
