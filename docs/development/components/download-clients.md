# Download client integrations

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

A download client integration lets Magpie hand a [release](../../using/search-and-download.md) to an [external download client](../../administration/storage-and-services.md#service-roles) and follow the transfer. You write one to support a client protocol Magpie does not already speak. Implement `IDownloadClientIntegration`, with a `ProviderType`, the `SupportedProtocols` it handles, and a [`SettingsSchema`](settings.md). `DisplayName` defaults to `ProviderType`.

Magpie sends a release only to a configured client whose integration lists the release's `Protocol` in `SupportedProtocols`, ignoring case. It tries enabled clients that are not `Unhealthy`, in order of priority and then name. Once a client accepts a release, the external download ID it returns is how Magpie follows the transfer and later asks to pause or remove it.

## Submission and polling

Each of the required methods receives the settings of the configured client and a cancellation token.

| Method | Input and result |
| --- | --- |
| `DownloadReleaseAsync` | Submit the `Release` and return `DownloadInfo`. |
| `GetDownloadAsync` | Read the external ID and return `DownloadInfo`. |
| `RemoveDownloadAsync` | Remove the external ID according to `deleteFiles` and return a Boolean. |
| `TestConnectionAsync` | Return a `ConfigurationTestResult` with `Success` and `Message`, and `FailureKind` on failure. |

`FailureKind` sets the client's health, as described in [Reporting test failures](indexers.md#reporting-test-failures), and Magpie does not send releases to an `Unhealthy` client. The external ID only has to be unique within that client, and later calls use the same ID and settings.

Magpie always calls the `DownloadReleaseAsync` overload that takes a `ReleaseGrabContext`, which carries `ContentType` and `IndexerConnectionId` but no idempotency token. Its default implementation calls the simpler overload. `Release.InfoUrl` is either a magnet URI or an http or https URL. For a URL, first call `IIndexerService.FetchPayloadAsync` with `context.IndexerConnectionId` and `release.InfoUrl` when the connection ID has a value, which uses the indexer's [authenticated session](indexers.md#fetching-protected-payloads). If it returns null, fetch the URL yourself. If the indexer fetch fails it throws a transient `ReleasePayloadRejectedException`, which you let propagate.

Map the client's state into `DownloadInfo`. This is a download in progress, with `Magpie.Models.Integrations` and `Magpie.Models.Enums`:

```csharp
return new DownloadInfo
{
    ExternalId = externalId,
    Status = DownloadStatus.Downloading,
    ProgressPercent = progressPercent,
    OutputPath = outputPath
};
```

`ProgressPercent` runs from 0 to 100, so half done is `50`, and null means unknown. Job progress through `IProgressReporter` is a fraction instead, so [do not copy one scale into the other](../using-built-in-services.md#submitting-a-job-and-report-progress). `OutputPath` is the path in the client's environment, or null until known. Magpie applies the configured [remote path mappings](../../administration/storage-and-services.md#remote-paths) to reach it, and [import](../../using/search-and-download.md#following-completion) needs a nonempty path Magpie can reach, so never invent a host path. Use `StatusReason` to explain a state.

Set `HeldByPolicy` when a client policy holds the task. Magpie then keeps polling the imported download, and the **Download cleanup** job does not remove it. To let administrators turn this wait off, declare `DownloadClientSettingsKeys.HonorClientHoldPolicy` from `Magpie.Models` in `SettingsSchema`. A successful forced start clears Magpie's recorded hold flag.

The [qBittorrent integration](../../../src/Magpie.Extensions.Qbittorrent/QbittorrentDownloadClientIntegration.cs) is a complete example.

## Download states

[`DownloadStatus`](../../../src/Magpie.Models/Enums/DownloadStatus.cs) mixes client transfer states with Magpie's own import states.

| Value | Meaning |
| --- | --- |
| `Grabbed` | The client accepted the release and has a task. |
| `Queued` | The download waits in the client's queue. |
| `Downloading` | The client is transferring data. |
| `Paused` | The transfer is paused. |
| `Stalled` | The transfer is making no progress. |
| `Downloaded` | The client transfer is complete. |
| `Importing` | Magpie is importing the files into the library. |
| `Imported` | Magpie finished the import. |
| `Cancelled` | A user stopped the download. |
| `Failed` | A permanent failure is recorded. |
| `Faulted` | A retryable or unknown failure occurred. |

Report `Downloaded` when the client is done. Magpie sets `Importing` and `Imported` itself. During refresh, Magpie stores a client-reported `Failed` as `Faulted`, so a client failure does not exclude the release for good. A Magpie record marked `Failed` blocks automatic grabs of the same release on that client, and an interactive grab can override that.

## Registration

Register the class in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point):

```csharp
services.AddScoped<IDownloadClientIntegration, ExampleDownloadClient>();
```

The contract is in `Magpie.Sdk.Integrations`. Providers are [additive](README.md#additional-components) by type, and Magpie uses the [last registration](README.md#registration-order) for a provider type. One [scoped](../using-built-in-services.md#service-lifetime-and-ownership) instance can receive calls for several configured clients, so read each call's settings and keep credentials and sessions separate per client. Use managed [HTTP clients and logging](../using-built-in-services.md#using-shared-facilities).

## Optional controls

[Optional capabilities](../../reference/extensions.md#optional-capabilities) go on the same object.

| Capability | Required meaning |
| --- | --- |
| `IPausableDownloadClient` | Pause the download. Return true if it is paused, including when it already was. Otherwise false. |
| `IResumableDownloadClient` | Resume the download. Return true if it is running, including when it already was. Otherwise false. |
| `IForcableDownloadClient` | `ForceStartDownloadAsync` starts immediately, bypassing queue and rate limits. Return true when the client accepts the request. |
| `IValidatesIntegrationSettings` | Check provider settings before use. |

An ordinary running state does not confirm a forced start. If a control response is lost, confirm with the client or propagate the error or cancellation. Do not report unconfirmed success.

Honor `deleteFiles` in `RemoveDownloadAsync`. Magpie calls it from these actions.

| Action | `deleteFiles` | If it returns `false` or throws |
| --- | --- | --- |
| **Stop** on the **Downloads** page | `false` | A `false` is ignored and the download is marked `Cancelled`. An exception leaves it active. |
| **Forget** on the **Downloads** page | Not called | Magpie deletes its own record only. |
| **Remove** on the **Downloads** page | `true` | The removal fails and Magpie keeps the record. |
| **Download cleanup** job | The client's **Delete files from client** setting, on by default | Magpie keeps the download and tries again on the next run. |

Return false when the download was not found. If a removal response is lost, propagate the error unless the client can confirm the removal, including file deletion when it was requested. A later call that finds no task returns false, which does not by itself confirm the files are gone.

## Failures and repeated calls

Throw `ReleasePayloadRejectedException` only when the client certainly added no download. After a timeout the result is uncertain, since the client can accept a request and lose the response. If it did accept and you report a rejection, Magpie tries the next eligible client and both download the release. The one-argument constructor makes a permanent rejection and the two-argument constructor sets `Transient`. Keep URLs, credentials, cookies, and passkeys out of the message. If every eligible client rejects the payload, Magpie records `Faulted` when all rejections are transient and `Failed` when any is permanent.

Only `ReleasePayloadRejectedException` lets Magpie fall back to another client. Any other submission exception, including a connection failure, stops the fallback and records `Faulted`. The **Release download item** [job](jobs.md) uses the `Fail` failure policy, so the runtime does not replay it, but later acquisition or manual requests can submit the same release again. Submission is therefore not exactly-once, and if the client supports task lookup or duplicate detection, use it to resolve an uncertain result before submitting again. Propagate cancellation even if the client already accepted the submission, since it can happen before Magpie saves a tracking record.

Override `GetDownloadsAsync` when the client can return several statuses at once. The default removes duplicate IDs, calls `GetDownloadAsync` for each, and records individual failures, and one inaccessible download must not turn into success for the whole batch. If the client confirms a task is absent, report `Failed` with the requested `ExternalId` and a `StatusReason`, as qBittorrent does, so refresh records `Faulted`. Do not infer `Cancelled` from absence, and do not return null. If a lookup fails, propagate the exception. If the whole batch throws, Magpie skips that client for this refresh, leaves each ID's last state alone, and polls again at the next scheduled refresh, every minute by default.

For pause, resume, forced start, and removal, Magpie makes one call with no immediate retry. Retry reads within bounded limits if you like, and repeat a write only when the protocol or confirmed state makes repeating safe.

See [IDownloadClientIntegration](../../../src/Magpie.Sdk/Integrations/IDownloadClientIntegration.cs), [DownloadInfo](../../../src/Magpie.Models/Integrations/DownloadInfo.cs), and [ReleasePayloadRejectedException](../../../src/Magpie.Models/ReleasePayloadRejectedException.cs).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
