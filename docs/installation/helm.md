# Helm chart

[Documentation](../README.md) / [Installation](README.md)

The Helm chart installs Magpie on Kubernetes as a single node or as a [cluster](../administration/clustering.md). It needs Kubernetes 1.23 or later, Helm 3.8 or later, and amd64 or arm64 nodes.

## Modes

Single node is the default. Set `cluster.enabled: true` for a cluster, whose pods share the database and a shared volume.

| | Single node | Cluster |
| --- | --- | --- |
| `replicaCount` | 1 only | 1 or more |
| Database | SQLite, PostgreSQL, or SQL Server | PostgreSQL or SQL Server |
| Extensions, keys, in-app backups | In the pod's data volume | In the shared volume |

Do not use `kubectl delete pod --force` on a Magpie pod, because the old process can keep running.

## Deciding before the install

The data volume's size, storage class, and claim cannot change after install. Moving the keys between files and Secrets gives Magpie different keys. The namespace's PodSecurity level limits which volumes you can use. See [Keys](#keys), [Storage](#storage), and [User and volume owners](#user-and-volume-owners). Secrets belong in the release's namespace. Add `--namespace <namespace>` to the commands here if it is not your current one.

## Installing

The chart is at `oci://ghcr.io/magpie-media-platform/charts/magpie`, and its version is the image version. A chart from a source checkout needs `--set image.tag=<version>`. The examples use the release name `magpie`, which also names the StatefulSet and Service. If your release name does not contain `magpie`, they are named `<release>-magpie`.

### Single node with SQLite

```sh
helm install magpie oci://ghcr.io/magpie-media-platform/charts/magpie --version <version>
```

### Single node with PostgreSQL or SQL Server

Put the connection string in a Secret:

```sh
kubectl create secret generic magpie-database \
  --from-literal=connection-string='Host=postgresql;Database=magpie;Username=magpie;Password=<password>'
```

Reference it in `values.yaml`:

```yaml
database:
  provider: postgresql   # or sqlserver
  existingSecret: magpie-database
```

```sh
helm install magpie oci://ghcr.io/magpie-media-platform/charts/magpie --version <version> --values values.yaml
```

With PostgreSQL the chart uses the `<appVersion>-postgresql` image, which includes the tools for in-app backup and restore.

### Cluster

A cluster needs the database Secret from above and a [shared volume](#shared-volume), here the claim `magpie-shared`.

```yaml
cluster:
  enabled: true
replicaCount: 3
database:
  provider: postgresql   # or sqlserver
  existingSecret: magpie-database
persistence:
  shared:
    existingClaim: magpie-shared
```

```sh
helm install magpie oci://ghcr.io/magpie-media-platform/charts/magpie --version <version> --values values.yaml
```

### Completing the setup

```sh
kubectl rollout status statefulset/magpie
kubectl port-forward service/magpie 8080:8080
```

Open [localhost:8080](http://localhost:8080) and complete [initial setup](../quick-start.md#completing-setup). Then follow [Downloading the first movie](first-download.md), using the container paths from [`extraVolumeMounts`](#media-root-folders-and-download-paths). For access without port forwarding, see [Ingress](#ingress).

## Configuration

Run `helm show values oci://ghcr.io/magpie-media-platform/charts/magpie --version <version>` for every value and its default. The ones you are most likely to set:

| Value | Default | Description |
| --- | --- | --- |
| `image.tag` | `""` | Empty uses `appVersion`, or `<appVersion>-postgresql` for PostgreSQL. |
| `cluster.enabled`, `replicaCount` | `false`, `1` | The mode and number of nodes. |
| `modeChange.to` | `""` | `cluster` or `single-node`, set only for an upgrade that changes the mode. See [Changing the mode](#changing-the-mode). |
| `database.provider` | `sqlite` | `sqlite`, `postgresql`, or `sqlserver`. |
| `database.existingSecret`, `database.connectionStringKey` | `""`, `connection-string` | The Secret and item with the connection string. |
| `keys.existingSecret`, `secretKey.existingSecret`, `secretKey.key` | `""`, `""`, `secret-key` | Keys from Secrets. See [Keys](#keys). |
| `knownProxies` | `[]` | Addresses or CIDR ranges of trusted reverse proxies. See [Ingress](#ingress). |
| `env` | `[]` | More environment variables for the Magpie container. |
| `persistence.data.*` | 10Gi, `ReadWriteOnce` | Each pod's data volume. Fixed after install. |
| `persistence.shared.volume`, `persistence.shared.existingClaim` | Empty | The cluster's shared volume. |
| `extraVolumes`, `extraVolumeMounts` | `[]` | Media and download locations. |
| `terminationGracePeriodSeconds` | `35` | Keep it at least 5 seconds above Magpie's 30 second shutdown timeout. |

Set [host settings](../reference/configuration.md) and variables such as `TZ` in `env`, and use `valueFrom.secretKeyRef` for secrets.

The startup probe allows 30 minutes and the liveness probe 10. If a first start or database restore takes longer, raise `startupProbe.failureThreshold` and `livenessProbe.failureThreshold`. The [readiness](../administration/observability.md#health-and-readiness) endpoint is `/api/system/ready`.

## Keys

By default Magpie creates its three [keys](../administration/maintenance.md#preserving-keys) as files in `/app/data/keys` on first start. That folder is in the data volume, or in the shared volume for a cluster. Changing `keys.existingSecret`, or in cluster mode `persistence.shared.*`, gives Magpie different keys, so copy the keys to the new location first.

### Keys from Secrets

Or supply the keys from Secrets:

```yaml
keys:
  existingSecret: magpie-keys
secretKey:
  existingSecret: magpie-secret-key
```

The Secret is mounted read-only at `/app/data/keys`, so `secretKey.existingSecret` is required and Magpie logs a harmless warning at each start. An extension that needs its own key file needs it added to that Secret. Do not put keys in values files or ConfigMaps.

#### Making new keys

```sh
for name in jwt-hmac api-key-hmac; do
  printf '{"Type":"Hmac","Material":"%s"}' "$(openssl rand -base64 32)" > "$name.key"
done
kubectl create secret generic magpie-keys --from-file=jwt-hmac.key --from-file=api-key-hmac.key
kubectl create secret generic magpie-secret-key --from-literal=secret-key="$(openssl rand -base64 32)"
rm jwt-hmac.key api-key-hmac.key
```

#### Keeping the keys of an existing installation

Copy `jwt-hmac.key`, `api-key-hmac.key`, and `integration-secret.key` if it exists from the old `/app/data/keys` into the current folder, and create the Secrets from them. Without `api-key-hmac.key`, existing API keys stop working.

```sh
kubectl create secret generic magpie-keys --from-file=jwt-hmac.key --from-file=api-key-hmac.key
```

Add `--from-file=integration-secret.key` if that file exists. For the second Secret, use the `MAGPIE_SECRET_KEY` value if the old installation set one, or `integration-secret.key`, which has the same format:

```sh
kubectl create secret generic magpie-secret-key --from-literal=secret-key='<the MAGPIE_SECRET_KEY value>'
kubectl create secret generic magpie-secret-key --from-file=secret-key=integration-secret.key
```

## Storage

### Data volume

Each pod mounts its own claim at `/app/data`, named `data-<name>-<ordinal>`, such as `data-magpie-0`. In single-node mode `persistence.data.existingClaim` uses a claim you made. In cluster mode, name an existing claim `data-<name>-<ordinal>` and the StatefulSet uses it.

Changing `persistence.data.*` after install makes `helm upgrade` fail. To grow a claim, expand the claim itself if its storage class allows it.

### Shared volume

In cluster mode every node mounts the `extensions`, `keys`, and `backups` folders of one shared volume. Set `persistence.shared.volume` to a volume source every Kubernetes node can mount, such as `hostPath` or `nfs`, or `persistence.shared.existingClaim` to a `ReadWriteMany` claim. If you set both, `volume` wins.

```yaml
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: magpie-shared
spec:
  accessModes:
    - ReadWriteMany
  storageClassName: <a class that supports ReadWriteMany>
  resources:
    requests:
      storage: 5Gi
```

For a folder at the same host path on every node:

```yaml
persistence:
  shared:
    volume:
      hostPath:
        path: /mnt/shared/magpie
        type: Directory
```

An NFS export with `root_squash` does not let the container set folder owners, so create the folders on the NFS server first, using your `PUID` and `PGID` if you set them:

```sh
mkdir -p /export/magpie/extensions /export/magpie/keys /export/magpie/backups
chown -R 1654:1654 /export/magpie
```

Avoid SMB and CIFS shares. They ignore file owners and modes, so everyone with access to the share can read the keys. Use NFS or a CSI volume that keeps POSIX owners and modes.

### Media root folders and download paths

Mount media and download locations with `extraVolumes` and `extraVolumeMounts`. Any Kubernetes volume source works:

```yaml
extraVolumes:
  - name: media
    nfs:
      server: nas.example.com
      path: /export/media
  - name: downloads
    hostPath:
      path: /mnt/shared/downloads
      type: Directory
extraVolumeMounts:
  - name: media
    mountPath: /media
  - name: downloads
    mountPath: /downloads
```

Use the container paths, such as `/media/movies`, for [root folders](../administration/storage-and-services.md#adding-a-root-folder). In a cluster, every node must mount each location at the same path, so use volumes all Kubernetes nodes can mount at once, such as NFS. If the download client sees different paths than Magpie, add a [remote path mapping](../administration/storage-and-services.md#remote-paths).

## Ingress

Magpie must be served at the root path of a host name. The controller needs an idle timeout longer than 30 seconds, for the live update stream at `/api/events/stream`, and a request body limit of at least 30 MB, for extension uploads. ingress-nginx allows 1 MB by default.

```yaml
ingress:
  enabled: true
  className: nginx
  annotations:
    nginx.ingress.kubernetes.io/proxy-body-size: "30m"
  hosts:
    - magpie.example.com
  tls:
    - secretName: magpie-tls
      hosts:
        - magpie.example.com
```

With another controller, set the body limit its own way. Set `knownProxies` to the addresses or CIDR range of the controller pods, such as `10.42.0.0/16` on k3s, so Magpie accepts [forwarded headers](../administration/access.md#publishing-a-network-address) only from them.

## Upgrade order

Back up the database and keys first (see [Backup and restore](#backup-and-restore)), read the release notes, then upgrade:

```sh
helm upgrade magpie oci://ghcr.io/magpie-media-platform/charts/magpie --version <version> --values values.yaml
kubectl rollout status statefulset/magpie
```

Afterwards, do the [upgrade checks](../administration/maintenance.md#verifying-the-upgrade).

## Changing the mode

Changing `cluster.enabled` on its own leaves your extensions and in-app backups behind, and makes new keys if they were in the volumes, which invalidates access tokens and API keys and leaves stored credentials undecryptable. Keys from Secrets are unaffected. The chart refuses a mode change until you set `modeChange.to`. Change `cluster.enabled` only with this procedure, and do not roll back over a mode change.

The procedure does not move a SQLite database, and a single node with `persistence.data.existingClaim` cannot become a cluster. To change the mode:

1. Stop Magpie:

   ```sh
   kubectl scale statefulset magpie --replicas=0
   kubectl wait --for=delete pod --selector app.kubernetes.io/instance=magpie --timeout=5m
   ```

2. When going to a cluster, create the [shared volume](#shared-volume).
3. Copy the `extensions`, `keys`, and `backups` folders with a pod. For single node to cluster, the source is `data-magpie-0` and the target is the shared claim. For the reverse, swap them, and for a `hostPath` or NFS shared volume use that volume source in place of the claim. The pod fails if a target folder is not empty.

   ```yaml
   apiVersion: v1
   kind: Pod
   metadata:
     name: magpie-copy
   spec:
     restartPolicy: Never
     containers:
       - name: copy
         image: busybox:1.36
         command:
           - sh
           - -c
           - |
             set -e
             for folder in extensions keys backups; do
               if [ -n "$(ls -A "/target/$folder" 2> /dev/null)" ]; then
                 echo "/target/$folder is not empty"
                 exit 1
               fi
             done
             for folder in extensions keys backups; do
               mkdir -p "/target/$folder"
               if [ -d "/source/$folder" ]; then cp -a "/source/$folder/." "/target/$folder/"; fi
             done
         volumeMounts:
           - name: source
             mountPath: /source
           - name: target
             mountPath: /target
     volumes:
       - name: source
         persistentVolumeClaim:
           claimName: data-magpie-0
       - name: target
         persistentVolumeClaim:
           claimName: magpie-shared
   ```

   ```sh
   kubectl apply --filename magpie-copy.yaml
   kubectl wait --for=jsonpath='{.status.phase}'=Succeeded pod/magpie-copy --timeout=30m
   kubectl delete pod magpie-copy
   ```

   If the wait fails, read `kubectl logs magpie-copy`, delete the pod, and apply again.

4. Change `cluster.enabled`, `replicaCount`, and the shared volume in `values.yaml`, then upgrade with `modeChange.to` set to `cluster` or `single-node`.

   ```sh
   helm upgrade magpie oci://ghcr.io/magpie-media-platform/charts/magpie --version <version> --values values.yaml \
     --set modeChange.to=cluster
   kubectl rollout status statefulset/magpie
   ```

The source folders stay where they were. Delete them once Magpie works. See [Uninstalling](#uninstalling).

## Backup and restore

Back up the database and the keys together. Without the keys from the time of the backup, stored credentials cannot be decrypted and existing API keys stop working. Back up:

- The database. For SQLite, use the [in-app backup](../administration/maintenance.md#creating-and-retaining-a-database-backup) or snapshot the data volume while the pod is stopped. For PostgreSQL and SQL Server, use the in-app backup or the database server's tools.
- The keys: the key Secrets, or the `keys` folder of the data volume (single node) or shared volume (cluster).
- The connection string Secret, if you use one.

The in-app backup of SQLite and PostgreSQL writes to `/app/data/backups` on the data or shared volume, so copy it elsewhere. SQL Server writes to the server's default backup folder.

To restore, put back the keys from the time of the backup. In a cluster, first scale to one replica (`kubectl scale statefulset magpie --replicas=1`). Restore with the [in-app restore](../administration/maintenance.md#restoring-through-magpie) or the database server's tools, then scale back.

## Rollback limits

`helm rollback` does not undo database migrations, so the earlier version never becomes ready. To go back after a migration, stop Magpie, restore the pre-upgrade database backup with its keys, and roll back:

```sh
kubectl scale statefulset magpie --replicas=0
helm rollback magpie
```

## Uninstalling

```sh
helm uninstall magpie
```

This keeps the data claims, the shared volume, all Secrets, the external database, and the media volumes. To reinstall onto the same data, use the same release name, values, and Secrets.

To delete all Magpie data permanently, delete the claims, the Secrets, and the folder of a `hostPath` or NFS shared volume yourself. This cannot be undone:

```sh
kubectl delete pvc --selector app.kubernetes.io/instance=magpie
kubectl delete pvc <the shared claim or existing data claim that you made>
kubectl delete secret magpie-keys magpie-secret-key magpie-database
```

## User and volume owners

Magpie runs as user and group 1654 by default. To use another, such as the owner of your media, set `PUID` and `PGID` in `env`:

```yaml
env:
  - name: PUID
    value: "1000"
  - name: PGID
    value: "1000"
```

Magpie's access to media comes from `PUID` and `PGID`. `fsGroup` and `supplementalGroups` do not apply.

The `baseline` PodSecurity level allows this but not `hostPath` volumes, so a `hostPath` shared or media volume needs `privileged`. The `restricted` level allows neither `hostPath` nor `nfs`. There, use claims for the shared and media volumes and run as non-root from the start:

```yaml
podSecurityContext:
  runAsNonRoot: true
  runAsUser: 1654
  runAsGroup: 1654
  fsGroup: 1654
  seccompProfile:
    type: RuntimeDefault
securityContext:
  allowPrivilegeEscalation: false
  capabilities:
    add: []
    drop:
      - ALL
```

This makes `PUID` and `PGID` do nothing. The `extensions`, `keys`, and `backups` subfolders in the shared volume must exist with owner UID 1654 before the first start. `hostPath`, NFS, and many `ReadWriteMany` volumes do not support `fsGroup`.

---

[Installation](README.md) | [Documentation](../README.md)
