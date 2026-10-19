# Installation

[Documentation](../README.md)

Magpie runs as a downloaded binary on Windows, Linux, and macOS. For a first installation, start with the [Quick start](../quick-start.md).

- [Windows service](windows.md): the installer registers Magpie as a Windows service.
- [Standalone application](standalone.md): the archive for Linux, macOS, and Windows, run from a terminal or under systemd.
- [Docker](docker.md): the container images and how to keep data and media outside the container.
- [Kubernetes](kubernetes.md): one Magpie instance from an example manifest.
- [Helm chart](helm.md): Magpie on Kubernetes as a single node or a multi-node cluster.

The installation method decides which [extensions](../administration/extensions.md) you start with. The Windows installer lets you pick them, and the Docker Standard image includes the first-party ones. A standalone archive has none.

SQLite is the default database. For PostgreSQL or SQL Server, prepare the database first, as described in [Databases](../administration/databases.md). A [cluster](../administration/clustering.md) needs a backplane provider, which PostgreSQL and SQL Server include.

After [setup](../quick-start.md#completing-setup), [Downloading the first movie](first-download.md) connects the services Magpie needs and checks a download from end to end. See also [Maintenance](../administration/maintenance.md) and [Troubleshooting](../administration/troubleshooting.md).

---

[Documentation](../README.md) | [Magpie overview](../../README.md)
