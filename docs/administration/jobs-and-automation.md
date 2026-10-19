# Jobs and automation

[Documentation](../README.md) / [Administration](README.md)

Everything Magpie does in the background runs as a job: searches, metadata refreshes, file imports, and maintenance.

## Jobs and executions

A job is a defined task, such as a search, a [metadata refresh](../power-user/metadata.md#verifying-a-metadata-refresh), or a [file import](../using/search-and-download.md#following-completion). An execution is one run of a job, with its own status, progress, and result.

Executions start from a schedule, from an event trigger (a named event such as a collection item being added), or by hand if the job allows it. Closing the page does not cancel anything.

**Admin > System Jobs** shows installation-wide work and the administrative triggers. Personal schedules and event triggers live in [My Jobs](../power-user/personal-automation.md).

## Following an execution

The **History** tab lists executions. Open one to see its progress, status message, trigger, parameters, and [node](clustering.md).

| State | Meaning |
| --- | --- |
| Pending | Waiting for a later time. The row shows **Pending until**. |
| Queued | Ready to run but not started yet. |
| Running | Work is in progress. |
| Completed | Finished without an error. |
| Failed | Ended with an error. A retry may follow. |
| Cancelled | Stopped after a cancellation. Changes made before it stopped stay. |

A job can create child work, so check the related executions. A retry is a separate execution linked to the original.

The **Retention cleanup schedule** runs daily at 03:00 UTC. By default it deletes finished executions after 30 days (`jobExecutionRetentionDays`) and audit records after 90 days (`auditRetentionDays`).

## Running one job

**Run a job** starts a job by hand. Pick the job, fill in its parameters, and follow the execution it returns.

A job appears only if it allows manual runs and your role is high enough for its [scope](../reference/roles.md#job-permissions): Requester for user jobs, Manager for content jobs, and Admin for administrative and system jobs. Jobs from a disabled [extension](extensions.md) are missing.

## Creating or changing a schedule

Managing schedules for the installation requires Admin.

In **Scheduled Jobs**, select **Add Schedule** or open an editable trigger, choose the job, and fill in its parameters. The timing is a preset or a five-part [**Cron Expression**](../power-user/personal-automation.md#schedule-timing). Times are UTC, so `0 5 * * *` runs daily at 05:00 UTC.

Search scans also apply the [minimum search age and the coalescing window](../reference/configuration.md#background-work), so a shorter interval does not search more often.

You cannot edit, create, or delete the built-in System-scope triggers. If one is missing, [Doctor](doctor.md) can restore it.

## Creating an event trigger

Select **Add Event Trigger**, choose a job, then set the event selection, optional [context filters](notifications.md#matching-event-context), parameters, and an optional **Delay (seconds)**. To send a message when an event happens, see [Notifications](notifications.md).

## Configuring retries

When a job fails, Magpie can retry it with a growing delay. The default policy is under **Admin > Advanced > Job retry backoff**:

| Control | Default |
| --- | --- |
| Base delay (seconds) | 10 |
| Backoff factor | 2 |
| Maximum delay (seconds) | 1800 |
| Maximum attempts | 5 |

The attempt limit counts the original run. A job's failure policy decides what happens: `Retry` and `Requeue` retry queued work with this policy, `Fail` never retries, and after an [immediate execution](api-and-cli.md#requesting-work-and-following-its-result) fails only `Requeue` creates a retry.

Retries do not fix problems that need a configuration change, such as bad credentials or an invalid path.

## Canceling or investigating stalled work

Cancelling requires Manager or higher. External work can run a little longer, and file operations that already completed stay in place. In a cluster, [Clustering](clustering.md#investigating-a-disconnection) covers a missed cancellation signal.

Queued work can be waiting for a free worker, a delayed retry, or a node that can start jobs. After a node fails, recovery can create another attempt, so check the result of the earlier operation before submitting a duplicate import or download. The worker count and stale-work limits are in the [configuration reference](../reference/configuration.md#background-work).

---

[Administration](README.md) | [Documentation](../README.md)
