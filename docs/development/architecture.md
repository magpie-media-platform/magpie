# Application boundaries

[Documentation](../README.md) / [Development](README.md)

Magpie runs the HTTP API, background work, and loaded [extensions](../administration/extensions.md) in one host process. The browser UI calls that API. The database stores the collection, configuration, and [job state](../administration/jobs-and-automation.md#jobs-and-executions). External services supply [metadata](components/metadata-providers.md), find releases, transfer downloads, and serve media.

## Following a title through the application

Adding a title and downloading it are separate steps, and each leaves its own record. A [metadata search](components/metadata-search.md) asks providers for candidates. Choosing one adds collection records and metadata, and downloads nothing. A release search then asks [indexers](components/indexers.md) for downloadable candidates for a collection item. Choosing a release queues background work, an external [download client](components/download-clients.md) does the transfer, and Magpie follows it and imports the resulting files.

An extension that presents these operations starts them through the application services and follows the returned job and download IDs to the result. An accepted response means only that the work is queued.

## Projects

The solution is split by responsibility.

| Area | Responsibility | Source |
| --- | --- | --- |
| Host | Startup, database selection, extension loading, and telemetry | [Magpie.App](../../src/Magpie.App) |
| API | Endpoint groups, HTTP results, and access requirements | [Magpie.Api](../../src/Magpie.Api) |
| UI | Collection and administration screens | [ui](../../ui/src) |
| Application services | Collection operations, repositories, integrations, jobs, and Doctor | [Magpie.Core](../../src/Magpie.Core) |
| Abstractions | Repository, transaction, and extension-lifecycle interfaces | [Magpie.Abstractions](../../src/Magpie.Abstractions) |
| Persistence | Shared Entity Framework model and database context | [Magpie.Data](../../src/Magpie.Data) |
| Database providers | SQLite, PostgreSQL, and SQL Server configuration and migrations | [Magpie.Data.SQLite](../../src/Magpie.Data.SQLite), [Magpie.Data.PostgreSQL](../../src/Magpie.Data.PostgreSQL), [Magpie.Data.SqlServer](../../src/Magpie.Data.SqlServer) |
| Command line | API command generation and command execution | [Magpie.CommandLine](../../src/Magpie.CommandLine), [Magpie.CommandLine.Generators](../../src/Magpie.CommandLine.Generators) |
| Query language | [MagpieQL](../reference/magpieql.md) parsing and translation | [Magpie.QueryLanguage](../../src/Magpie.QueryLanguage) |
| Authentication | [Authentication services](components/authentication.md) | [Magpie.Auth](../../src/Magpie.Auth) |
| Extension contracts | [Component contracts](components/README.md) | [Magpie.Sdk](../../src/Magpie.Sdk) |
| Shared models | Requests, results, entities, and configuration definitions | [Magpie.Models](../../src/Magpie.Models) |
| Windows installer | MSI package with a feature for each first-party extension | [Magpie.Installer](../../src/Magpie.Installer) |

Endpoint groups apply access requirements and call application services, and the services coordinate stored state, integrations, and events. An extension that writes to the database directly skips part of that coordination, which the [built-in services](using-built-in-services.md) keep. An extension endpoint must also enforce the [required role](../reference/roles.md) itself, because not every service method checks HTTP authorization.

## Requests, jobs, and Doctor

An HTTP request is short-lived. Long-running work goes through the job system, which keeps state and progress in an execution record. A component submits work through [`IJobManager`](using-built-in-services.md#submitting-a-job-and-report-progress), and a job handler reports progress through `IProgressReporter`. Each execution gets its own service scope, which ends when the handler finishes. See [Jobs](components/jobs.md).

Doctor runs as a job. A scan gives each registered check the configured entities and a cancellation token, and a check that completes replaces its stored findings. A finding can name a repair, but the scan never runs it. An administrator requests the repair separately, and the repair has to confirm that the finding still applies. The [Doctor walkthrough](diagnostics.md) shows this.

## Extension loading and lifetime

A package on disk, an installed database record, and an active component are different things. At startup the host picks the extensions to load. On a new instance it discovers package directories. Once extensions have been installed, changed, or removed through Magpie, it uses the database records instead, even if that list is empty. The loader reads each assembly, checks its [ABI compatibility](../reference/extensions.md#framework-and-abi-compatibility), creates the [`IExtension`](../reference/extensions.md#entry-point) entry point, and calls `RegisterServices`. The host then builds the service provider, and extensions with [lifecycle hooks](components/lifecycle.md) get their install, upgrade, and initialization calls.

`RegisterServices` runs before the final provider exists, so it can only register types. Components get their dependencies through their constructors.

Installing or changing a package updates the files and the installed state, but the running process keeps what it loaded. A restart activates the change. If the extension does not load after the restart, the host log has the load error.

An extension can [add components](components/README.md#additional-components) or [replace a documented default](components/README.md#replacement-components). The [extension tutorial](extensions.md) starts with a settings declaration.

## Persistence and multiple nodes

SQLite suits a single node. For several nodes, the database holds the authoritative state and a [backplane](components/cluster-backplane.md) carries synchronization signals between them. A cluster needs a backplane. Today the PostgreSQL and SQL Server providers supply one, more are planned, and an extension can supply its own. See [Clustering](../administration/clustering.md).

Each node serves the UI, the API, and the job runner unless setup limits it to some of those roles, which it saves in `Magpie:Cluster:Roles`. A job can therefore run on a different node than the one that received the request. The backplane is best-effort and does not replay missed signals, so a consumer that keeps a view of stored state must read it from the database again after a gap.

---

[Development](README.md) | [Documentation](../README.md)
