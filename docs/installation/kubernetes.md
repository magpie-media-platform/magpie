# Kubernetes deployment

[Documentation](../README.md) / [Installation](README.md)

The [example manifest](kubernetes.yaml) runs one Magpie instance with [SQLite](../administration/databases.md), a claim for application data, and a claim for media. For several nodes, or to install with values instead of editing a manifest, use the [Helm chart](helm.md#cluster).

## Preparing the namespace and storage

Running Magpie on Kubernetes requires a Linux node on AMD64 or ARM64 that can pull `ghcr.io/magpie-media-platform/magpie`, and `kubectl` access to a namespace. Replace `NAMESPACE` in the commands below with yours. Create these two claims in it first:

| Claim | Mount | Purpose |
| --- | --- | --- |
| `magpie-data` | `/app/data` | SQLite, configuration, [keys](../administration/maintenance.md#preserving-keys), and installed [extensions](../administration/extensions.md). The file system must support SQLite file locking. |
| `magpie-media` | `/media` | Completed downloads and library destinations, shared with the download client. |

The media layout is the one from the [first download](first-download.md#connecting-storage), and in Magpie you enter paths under `/media`.

Magpie runs as UID 1654 and GID 1654 by default. Set `PUID` and `PGID` in the container environment to use another account, which needs read access to downloads and write access to `/media/movies`. If the download client reports different paths, add a [remote path mapping](../administration/storage-and-services.md#remote-paths).

Keep one replica. Two pods must never open the same SQLite database.

## Preparing the manifest

Copy [kubernetes.yaml](kubernetes.yaml) and replace `VERSION` in the image tag with the release version, without a leading `v`. The image is Standard, with the first-party extensions. `VERSION-bare` leaves extensions to be installed during setup.

The manifest creates a Deployment named `magpie` and a Service on port `8080`. Set resource limits for your cluster, and see [observability](../administration/observability.md) for what to set. A memory limit that kills the pod can interrupt imports.

The keys are in the data claim, so recovery needs the whole claim.

### Using an external database

PostgreSQL or SQL Server can replace SQLite without touching the media mount. Prepare the database as described in [Databases](../administration/databases.md), and with PostgreSQL use the image tag `VERSION-postgresql` for backup and restore through Magpie. Keep the connection string in a Secret, as in this `env` fragment:

```yaml
env:
  - name: Magpie__Database__Provider
    value: postgresql
  - name: Magpie__Database__ConnectionString
    valueFrom:
      secretKeyRef:
        name: magpie-database
        key: connection-string
```

See the [configuration reference](../reference/configuration.md). The data claim is still needed. If you supply `MAGPIE_SECRET_KEY` yourself, provide the same value from a Secret on every replacement pod.

## Deploying and completing setup

```sh
kubectl -n NAMESPACE get pvc magpie-data magpie-media
kubectl -n NAMESPACE apply -f kubernetes.yaml
kubectl -n NAMESPACE rollout status deployment/magpie
```

If the rollout stalls, look at the pod and its events:

```sh
kubectl -n NAMESPACE get pods -l app=magpie
kubectl -n NAMESPACE describe pods -l app=magpie
kubectl -n NAMESPACE logs deployment/magpie --tail=100
```

Then open a local connection and complete [initial setup](../quick-start.md#completing-setup) at [localhost:8080](http://localhost:8080):

```sh
kubectl -n NAMESPACE port-forward service/magpie 8080:8080
```

If Magpie does not come back after a restart, run `kubectl -n NAMESPACE rollout restart deployment/magpie` and start the port forward again. Afterwards, follow [Downloading the first movie](first-download.md) and run [Doctor](../administration/doctor.md).

## Providing persistent browser access

For everyday use, route an ingress or proxy to service `magpie`, port `8080`. See [Access](../administration/access.md) for trusted proxies and forwarded headers. Services Magpie uses need addresses the pod can reach, such as Kubernetes DNS names, because `localhost` is the pod.

## Upgrade, recover, and remove

First preserve the database, application data, external secrets, and media, as described in [Maintenance](../administration/maintenance.md). Then change the image tag, apply the manifest, and wait for the rollout.

An image rollback does not undo database changes. To go back to an older database, stop the deployment and restore the matching state before starting it.

```sh
kubectl -n NAMESPACE delete -f kubernetes.yaml
```

That removes the Deployment and Service. The claims and Secrets remain, and deleting a claim can delete its storage, depending on the provider's reclaim policy.

---

[Installation](README.md) | [Documentation](../README.md)
