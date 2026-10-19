# Clustering

[Documentation](../README.md) / [Administration](README.md)

A cluster is several Magpie nodes sharing one database. For Kubernetes, the Helm chart's [cluster mode](../installation/helm.md#cluster) sets this up.

A cluster needs a backplane provider. PostgreSQL and SQL Server include one, and an extension can supply another.

## Preparing shared state and node identity

Every node needs:

- The same Magpie version, database, and [extensions and settings](extensions.md).
- A unique, stable node ID, such as `Magpie__Cluster__NodeId=magpie-2`.
- The same media and download paths.
- The same `keys` directory, copied from the first node before another starts, or one shared mount. `MAGPIE_SECRET_KEY`, if set, must be identical everywhere.

A node that starts without the key files creates new ones and can no longer decrypt stored credentials. See [Preserving keys](maintenance.md#preserving-keys).

## Selecting extension storage

`Magpie:Cluster:Mode` is `MultiNodeSharedExtensions` (all nodes read one extension directory) or `MultiNodeLocalExtensions` (each node has its own, and local `.nupkg` uploads are unavailable).

```text
Magpie__Cluster__Mode=MultiNodeSharedExtensions
Magpie__Extensions__Path=/shared/magpie/extensions
```

## Adding a node

On the administrator step of the new node's setup page, select **Join an existing cluster**, enter the shared **Connection string**, and choose the **Roles** and **Job types**. Restart the node afterwards. Containers need an explicit node ID.

`Magpie:Cluster:Roles` can be `Ui`, `Api`, and `JobRunner`, and an empty list serves all three. `Jobs:JobTypes` limits the job types a runner starts. In the environment, use numbered keys such as `Magpie__Cluster__Roles__0=Api`.

## Verifying a node before normal use

Open **Admin > System Status** through the node's own address and check **Node ID**, then run an operation and check the execution's **Node** in **Admin > System Jobs**. A load balancer can hide differences, so check each node directly.

A node that runs no work is usually stale, has a wrong node ID, or has a restrictive `Jobs:JobTypes`. [Doctor](doctor.md) reports these. "No handler is registered for job type" means the node lacks the extension for that job.

## Coordinating extension changes

After an extension change every node is stale until it restarts. Make the packages available to every node, then restart them. Take a [recovery set](maintenance.md#preparing-a-recovery-set) first.

## Investigating a disconnection

Cross-node events are best effort, and a node that was disconnected does not get missed events replayed. After connectivity returns, check the database connection and logs on each affected node, and check what happened before resubmitting work.

If the log says "The cluster backplane worker failed and stopped cross-node event distribution", restart that node.

If a cancelled execution stays **Running**, cancel again through the owning node's address, or restart that node, which stops all its jobs. See [Troubleshooting](troubleshooting.md).

---

[Administration](README.md) | [Documentation](../README.md)
