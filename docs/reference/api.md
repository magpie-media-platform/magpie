# HTTP API reference

[Documentation](../README.md) / [Reference](README.md)

The Magpie HTTP API lives under the `/api` prefix and uses JSON request and response bodies. This page lists the common routes: collection reads, [job execution](../administration/jobs-and-automation.md#jobs-and-executions), notification rules, and the rules shared by all requests. For scripting, see [API and command-line use](../administration/api-and-cli.md).

**On this page**

- [Authentication and request format](#authentication-and-request-format)
- [Collection reads](#collection-reads)
- [Creating a shared filter from a wishlist](#creating-a-shared-filter-from-a-wishlist)
- [Job discovery and execution](#job-discovery-and-execution)
- [Finding executions from a personal trigger](#finding-executions-from-a-personal-trigger)
- [Notification rules](#notification-rules)
- [Failures and diagnostic reads](#failures-and-diagnostic-reads)

## Authentication and request format

Send the [API key](../administration/access.md#issuing-an-api-key) of the account in the `X-Api-Key` header. A key uses the current permissions of its owner and gives no other access.

JSON bodies use `Content-Type: application/json`, camel-case property names, and enum names such as `Queue` or `Completed`. Magpie rejects unknown JSON properties with HTTP 400.

This request reads the first page of the collection, with the key in `MAGPIE_API_KEY`:

```sh
curl --fail-with-body \
  -H "X-Api-Key: $MAGPIE_API_KEY" \
  "http://localhost:8080/api/content?pageNumber=1&pageSize=25"
```

It reads the collection and does not search external [metadata providers](../administration/metadata.md) or [indexers](../administration/storage-and-services.md#service-roles).

## Collection reads

These operations require [Viewer or a higher role](roles.md).

| Method and path | Result |
| --- | --- |
| `GET /api/content` | A page of collection items. |
| `GET /api/content/{id}` | One collection item, or HTTP 404 if absent. |
| `GET /api/content/{id}/children` | A page of child items. |
| `GET /api/content/stats` | Collection statistics. |

`{id}` is the GUID of a record. `GET /api/content` accepts these query parameters:

| Parameter | Meaning |
| --- | --- |
| `contentType`, `level` | [Content type and hierarchy-level](../using/collection.md#content-types-and-hierarchy) identifiers. The Movies [extension](../administration/extensions.md) uses `movie` for both. |
| `parentId` | Parent GUID. Selects the direct children of that item. |
| `topLevelOnly` | `true` restricts the result to top-level items. |
| `rootFolderId` | [Root folder](../administration/storage-and-services.md#adding-a-root-folder) GUID. Matches the effective root folder, including inheritance. |
| `monitoring` | [Monitoring](../using/monitoring-and-wishlists.md#setting-monitoring): `true` selects items whose effective mode is Monitored or Shotgun. `false` selects items whose effective mode is Unmonitored. |
| `fulfillment` | [Media fulfillment state](magpieql.md#content-fields): `Missing`, `Upgradable`, `Fulfilled`, `FulfilledExternally`, or `Partial`. |
| `search` | Text contained in the stored hierarchy name. |
| `tag` | A [tag](../using/collection.md#subjects-and-tags) value to match. |
| `filter` | A URL-encoded [MagpieQL expression](magpieql.md). |

[MagpieQL](magpieql.md) defines the filter syntax and the operators each field supports. A malformed filter returns HTTP 400 with `position` and `expected` fields. A filter that parses but uses an operator its field does not support fails when the query runs, and can return a server error.

The `monitoring` parameter takes a Boolean. The `monitoring` field inside a `filter` takes mode names and can tell Shotgun from Monitored.

### Pagination

Collection and job lists default to `pageNumber=1` and `pageSize=25`. Page numbers below 1 become 1. Page size is limited to 1 through 1000.

A paged response has these fields:

| Field | Meaning |
| --- | --- |
| `items` | Records in this page. |
| `pageNumber` | The returned page number. |
| `pageSize` | The selected page size. |
| `totalItems` | The total number of matching records. |

An empty collection returns HTTP 200 with no items.

## Creating a shared filter from a wishlist

A [wishlist](../using/monitoring-and-wishlists.md#creating-and-verifying-a-wishlist) can save its criteria as a separate shared filter. All accounts can see the copy, and only an Admin can edit it, even when a Requester creates it. See [filter ownership](roles.md#saved-filter-ownership).

To create the copy, an account with Requester access or higher updates a wishlist it owns:

1. Send `GET /api/wishlists/my` to find the `id` of the wishlist.
2. Send `GET /api/wishlists/{id}` and read `contentFilter.criteria`.
3. Send `PATCH /api/wishlists/{id}` with these body fields:

| Body field | Value |
| --- | --- |
| `criteria` | An object with one property, `value`, that contains the complete criteria object from step 2. |
| `saveAsReusableFilter` | `true`. |

Send the structured criteria object, not a MagpieQL string or the whole `contentFilter` object. The flag alone does not create a copy.

HTTP 204 confirms the update. The copy appears under **Filters** with the wishlist's name and does not stay synchronized with it. If the wishlist uses a filter that another wishlist also uses, or a filter with a different owner, the request also creates a personal filter named `NAME (wishlist)` and attaches it to the wishlist. Each successful request creates another shared copy.

## Job discovery and execution

A job type describes work that Magpie can run. An execution is one attempt to do that work.

### Finding job types and executions

| Method and path | Result |
| --- | --- |
| `GET /api/jobs/specs` | Registered job types, parameters, scopes, and trigger capabilities. |
| `GET /api/jobs` | A page of executions. |
| `GET /api/jobs/{id}` | One execution and its current status. |
| `GET /api/jobs/{id}/execution-tree` | Related executions, with a default limit of 500 nodes. |

For an execution tree, set `max-nodes=N` to change the limit. `N` must be at least 1. A smaller value returns HTTP 400. Values above 5000 become 5000. Set `include-siblings=true` to include sibling executions.

Read operations require Viewer. Enqueue and immediate execution require at least Requester, and the scope of the job sets the minimum role. The job must also permit manual execution: its specification has `onDemand: true` and `supportedTriggerModes` contains `Manual`. A request that fails a check returns HTTP 400 with the reason in `detail`. Cancellation requires Manager. See [job permissions](roles.md#job-permissions).

### Filtering execution history

For job lists, repeat `status` to select several states. For example, `status=Failed&status=Cancelled` selects those two states. Unknown status names return HTTP 400.

`status=Pending` selects queued executions whose start time is in the future. Do not combine it with other status values.

`mine=true` selects executions that the authenticated account created and makes `rootsOnly` default to true. Set `rootsOnly=false` to include child executions.

### Finding executions from a personal trigger

A [personal schedule or event trigger](../power-user/personal-automation.md) runs its jobs as the system account, so `mine=true` on a jobs request excludes those runs. On the trigger-list request, `mine=true` selects the account's own triggers. Requester access is enough.

1. For a schedule, send `GET /api/schedule-triggers?mine=true`. For an event trigger, send `GET /api/event-triggers?mine=true`.
2. Find the trigger by `name` in the returned array and copy its `id`.
3. Send `GET /api/jobs?scheduleTriggerId={id}` for a schedule or `GET /api/jobs?eventTriggerId={id}` for an event trigger.
4. Find the run in the [paged](#pagination) `items` by `createdAt` and `parameters`, and read its `status` and `statusMessage`.
5. Send `GET /api/jobs/{id}` to follow its [state](#execution-states).

An administrator can also read these runs in [System Jobs](../administration/jobs-and-automation.md#following-an-execution).

### Submitting work

| Method and path | Result |
| --- | --- |
| `POST /api/jobs/enqueue` | HTTP 202 and the accepted execution ID. |
| `POST /api/jobs/execute-immediate` | An execution ID, status, and status message. The request waits while the job runs on the node that received it. The returned status can be `Failed`. |
| `POST /api/jobs/{id}/cancel` | A cancellation request. HTTP 204 when accepted. |

The enqueue body uses these fields:

| Field | Requirement and meaning |
| --- | --- |
| `jobType` | Required job type identifier from the registered specifications. |
| `extensionId` | Extension identifier, for a job that an extension provides. |
| `parameters` | Object of parameter names and string values that the job defines. |
| `mode` | Accepted by the shared request model, but the endpoint selects the mode. Omit it in these requests. |

The job's specification defines its parameters. `jobType` is the identifier, not the display label. `/enqueue` always selects `Queue`. `/execute-immediate` always selects `Immediate` and requires the `supportsImmediate` capability in the job specification.

The enqueue response body is a JSON string containing the execution GUID, and its `Location` header identifies the execution. HTTP 202 means only that Magpie accepted the request.

### Execution states

| Status | Meaning |
| --- | --- |
| `Queued` | Waiting for execution. |
| `Running` | Work is in progress. |
| `CancellationRequested` | Cancellation was requested. The work has not stopped yet. |
| `Completed` | This execution completed without an error. |
| `Failed` | This execution failed. |
| `Cancelled` | This execution was cancelled. |

`statusMessage` gives more detail. `parentExecutionId`, `originalExecutionId`, and the execution tree link related work and retries. A completed execution does not show the result of its related executions.

## Notification rules

See [Notifications](../administration/notifications.md) for rule matching and delivery. Through the API, Requesters can read and test their own rules, Managers can also create, change, and delete their own rules, and administrators can manage all rules.

| Method and path | Minimum role and result |
| --- | --- |
| `GET /api/notification-rules` | Requester. List the accessible rules. |
| `GET /api/notification-rules/{id}` | Requester. Read an accessible rule. |
| `POST /api/notification-rules/{id}/test` | Requester. Test the delivery settings of a saved rule. |
| `POST /api/notification-rules` | Manager. Create a rule. |
| `PATCH /api/notification-rules/{id}` | Manager. Change an accessible rule. |
| `DELETE /api/notification-rules/{id}` | Manager. Delete an accessible rule. |
| `POST /api/notification-rules/test` | Manager. Test unsaved settings. |

For an account that is not an administrator, a new rule belongs to the caller, and read and test access depend on ownership. A delivery test skips event matching. See [Testing the provider, then the rule](../administration/notifications.md#testing-the-provider-then-the-rule).

## Failures and diagnostic reads

| Result | Interpretation |
| --- | --- |
| HTTP 400 | Magpie rejected the request or its values. This includes a job request that fails the job scope or manual-execution check. Read the response detail. |
| HTTP 401 | Authentication did not succeed. |
| HTTP 403 | The role of the account is below the minimum role of the route. |
| HTTP 404 | The resource does not exist, or the account cannot access it. An example is the wishlist, filter, or notification rule of another account. |
| HTTP 405 | The route exists, but not for this HTTP method. The `Allow` header lists the permitted methods. |
| HTTP 409 | A restart, shutdown, or database restore request cannot run, because the host is not supervised or a shutdown is already in progress. An Interactive Search grab also returns 409 when no download client is available or an acquisition is already in progress. |
| HTTP 503 during database startup | The application cannot serve the request yet. |
| HTTP 503 with `X-Magpie-Setup-Required: true` | Initial setup is not complete. Unauthenticated API requests are blocked until setup is complete. |
| Successful HTTP response followed by a failed job | The request succeeded, and the later execution failed. Read the job. |

An error response has `status`, `title`, and `detail`.

`GET /api/diagnostics/findings` returns [Doctor findings](../administration/doctor.md) to Viewer and higher roles. The anonymous health and readiness endpoints have separate meanings. See [Telemetry](telemetry.md#health-endpoints).

Collection deletion, download removal, file deletion, and file unlinking are separate operations with different effects. See [Files](../using/files.md) and [Search and downloads](../using/search-and-download.md).

This page does not list every route, and the release includes no OpenAPI document. The [endpoint groups](../../src/Magpie.Api/EndpointGroups) and [models](../../src/Magpie.Models) in the source define the rest, and [CLI help](commands.md) lists the generated commands. Extension endpoints are documented with their extension.

---

[Reference](README.md) | [Documentation](../README.md)
