# Using built-in services

[Documentation](../README.md) / [Development](README.md)

A [component](components/README.md) usually needs existing Magpie behavior as well as its own. A [Doctor check](components/doctor-checks.md) reads [settings](components/settings.md), an [endpoint](components/api-endpoints.md) starts a search, and a [job](components/jobs.md) reads [metadata](components/metadata-providers.md) and reports progress. The component is registered in [`IExtension.RegisterServices`](../reference/extensions.md#entry-point) and receives these services as constructor dependencies.

Only the services listed in the [supported consumption reference](../reference/extensions.md#supported-service-consumption) are meant to be used by extensions. A public interface that is not listed there is not supported.

## Service lifetime and ownership

Application services for content, settings, downloads, diagnostics, and file access are scoped, meaning one instance per request or operation. A component that uses them is registered as scoped too, as the [Scratch check](diagnostics.md) is:

```csharp
services.AddScoped<IDiagnosticCheck, ScratchDirectoryCheck>();
```

A singleton that captures a scoped service keeps it after its scope ends, and can later call a service whose database context is already disposed. A [hosted worker](#observing-events-in-a-hosted-worker) therefore creates a scope for each operation through `IServiceScopeFactory`, awaits the work inside it, and disposes it afterward.

Each kind of call has its own lifetime.

| Host call | Available lifetime | What to do |
| --- | --- | --- |
| `RegisterServices` | Registration only, before the provider exists | Register types and factories. Do not start work. |
| Job execution | A scope for the execution | Use injected services and finish the work before returning. |
| [Lifecycle hook](components/lifecycle.md) | A scoped provider in `ExtensionLifecycleContext` | Resolve services inside the hook and do not keep them. |
| HTTP endpoint | The request scope | Await the work, or submit longer work as a job. |

Dependency injection disposes what it creates, and the extension disposes what it owns, such as streams, responses, scopes, and event listeners. The cancellation token passes through asynchronous calls, and cancellation propagates.

## Reading collection data and metadata

Each kind of state has an owning service. `IContentService` covers local content, the [collection hierarchy](components/media-types.md#modeling-a-hierarchy-and-its-paths), and [file records](../using/files.md#inspecting-and-correcting-file-associations). `IMetadataService` covers combined metadata, [contributions](../power-user/metadata.md#inspecting-a-conflicting-value), and metadata edits. `IMetadataSearchService` searches external metadata providers. `ISubjectService`, `ITagService`, and `IWishlistService` cover [subjects](components/subject-types.md), [tags](../using/collection.md#subjects-and-tags), and [wishlists](../using/monitoring-and-wishlists.md#creating-and-verifying-a-wishlist).

A search result can exist before the title is in the collection, and combined metadata is the result of all contributions plus local edits. This component reads the combined metadata of a [content hierarchy item](components/media-types.md#modeling-a-hierarchy-and-its-paths):

```csharp
using Magpie.Models.Data;
using Magpie.Sdk.Services;

internal sealed class MetadataReader(IMetadataService metadata)
{
    public Task<MaterializedMetadata?> ReadAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken) =>
        metadata.GetContentMetadataAsync(contentHierarchyItemId, cancellationToken);
}
```

Register it with `services.AddScoped<MetadataReader>();`. A `null` result means the item does not exist or has no metadata. A `ContentItem` supplies its `ContentHierarchyItemId`, not its own `Id`. The [metadata service contract](../../src/Magpie.Sdk/Services/IMetadataService.cs) names the identifier each operation takes.

## Searching for a release and request acquisition

`IReleaseSearchService` searches indexers for a collection item and starts the download of a chosen release. In an asynchronous method with an injected `searches`:

```csharp
var result = await searches.SearchInteractiveResultAsync(
    contentHierarchyItemId,
    cancellationToken);
```

A `null` result means the item does not exist. Otherwise `Candidates`, `Partial`, and `Warning` are read together, because an empty complete result and an incomplete search are different outcomes. After a caller picks a candidate, pass its `Release.Id` and the caller's user ID:

```csharp
var outcome = await searches.GrabInteractiveAsync(
    contentHierarchyItemId,
    releaseId,
    actingUserId,
    cancellationToken);
```

`outcome.Status` is `Accepted` when an acquisition job is queued, and then `ExecutionId` identifies it. The other values cover an invalid release, missing content, no enabled download client, and an acquisition already in progress, with `Detail` when present. Accepted only means queued. Follow the work with [`IJobManager`](#submitting-a-job-and-report-progress), `IDownloadService` for transfers, and `ILibraryImportService` for imports.

The services that manage Magpie's side of integrations are `IIndexerService`, `IDownloadClientService`, `IMediaServerService`, `IRootFolderService`, `IRemotePathMappingService`, and `IQualityService`. A new integration follows its component guide.

## Submitting a job and report progress

`IJobManager.EnqueueAsync` queues a job for a user request. It takes the request and the ID and role of the authenticated caller, and applies the caller checks. `ExecuteImmediateAsync` does the same and waits for the result, and throws `MagpieException` for a job type that does not support immediate execution. `ExecuteAsync` is the trusted internal path and does not replace the caller checks. For a registered extension job:

```csharp
var executionId = await jobs.EnqueueAsync(
    new Magpie.Models.Jobs.JobEnqueueRequest
    {
        JobType = jobType,
        ExtensionId = extensionId,
        UserId = callerUserId
    },
    callerUserId,
    callerRole,
    cancellationToken);
```

The `executionId` follows the operation through `GetExecutionAsync`, or `GetExecutionTreeAsync` when related work has its own executions. Inside a job handler, `IProgressReporter` reports a fraction from `0.0` to `1.0`, or `null` when progress is indeterminate:

```csharp
progress.Report(0.5, "Checked half of the items.");
await progress.FlushAsync(cancellationToken);
```

Cancellation is checked between units of work, and the token goes to external calls. The [job guide](components/jobs.md) covers handler registration, parameters, and failure behavior.

## Publishing and observing events

An event tells subscribers that something happened. It does not replace stored state that a later reader needs. For work that needs history, progress, or cancellation, use a job with an event trigger instead, as described in [Jobs](components/jobs.md#running-a-job-from-an-event-or-schedule). Otherwise publish with `IEventBus` and observe with an `IEventListener` in a hosted worker.

### Publishing an event

This method publishes an extension event, using an injected `IEventBus events` and `TimeProvider time`:

```csharp
public Task PublishReadyAsync(CancellationToken cancellationToken)
{
    var now = time.GetUtcNow().UtcDateTime;
    var ready = new Magpie.Models.MagpieEvent(
        Guid.NewGuid(),
        "scratch.ready",
        new Dictionary<string, string>(),
        now,
        now,
        null);

    return events.PublishAsync(ready, cancellationToken);
}
```

With a `null` node ID the event bus applies the local node identity and the job and trace correlation of the current execution. A transported event keeps its identity and origin.

### Observing events in a hosted worker

An extension can run a long-lived worker as a hosted service. The host starts it, cancels its token at shutdown, and stops if it throws, so expected failures need catching and logging inside the loop. Every node runs the worker. This one listens for the event above:

```csharp
using Magpie.Sdk.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal sealed class EventObserver(IEventBus events, ILogger<EventObserver> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var listener = events.CreateListener(
            "scratch.ready",
            localOnly: false,
            callSite: nameof(EventObserver));
        await foreach (var item in listener.ReadAllAsync(stoppingToken))
        {
            logger.LogInformation("Received event {EventId}", item.Id);
        }
    }
}
```

Register it in `RegisterServices`:

```csharp
services.AddHostedService<EventObserver>();
```

A listener only receives events that arrive after it starts. Handling that needs scoped services creates a scope for each event. In a [cluster](../administration/clustering.md), a node can miss a signal, and a completed `PublishAsync` does not confirm delivery to other nodes. See the [backplane contract](components/cluster-backplane.md) for the delivery limits.

## Reading configuration and application state

`ISettingsService` reads extension-owned settings, and values can be absent or change between operations. `IUserService` reads and manages accounts, so the caller's identity and required access apply. `IExtensionService` manages installed extensions, and package changes apply after a restart. `IDiagnosticsService` reads findings and requests repairs, and a stored finding can be out of date. `IMagpieSystem` reads host and node information, and whether the host can restart depends on how it was started.

Read a settings group with its extension ID and group name. `SettingsGroupDetail.Group.Settings` holds the saved values. They are read at operation time rather than in a constructor, so a saved change takes effect on the next operation. The [Doctor walkthrough](diagnostics.md#adding-the-repair) does this before a repair so it does not act on an old path. Never log or return secret values. The [settings guide](components/settings.md) covers schemas and configuration tests.

## Using shared facilities

The standard .NET facilities apply where they exist.

| Facility | How |
| --- | --- |
| Logging | Inject `ILogger<T>` and use structured values that identify the operation. Omit secrets. |
| HTTP | Register a named or typed client with `AddHttpClient`, use `IHttpClientFactory`, and dispose responses. |
| Time | Inject `TimeProvider` and use `GetUtcNow()`, with controlled time in tests. |
| Files | Inject `IFileSystem`, use paths the host can see, and fake it in unit tests. |
| Caching | Inject [`ICacheFactory`](components/caching.md#preserving-cache-behavior). |

`ICacheFactory.GetOrCreate` returns one named cache shared by all scopes, which suits reusable data. `CreateScoped` returns a private cache for the current scope. Do not put scoped service instances into shared cached values. See [Caching](components/caching.md) to supply a custom cache. The [telemetry reference](../reference/telemetry.md) covers trace and metric conventions.

---

[Development](README.md) | [Documentation](../README.md)
