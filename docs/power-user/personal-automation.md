# My Jobs

[Documentation](../README.md) / [Power-user guide](README.md)

[Jobs](../administration/jobs-and-automation.md#jobs-and-executions) do Magpie's background work, such as [metadata refresh](metadata.md#verifying-a-metadata-refresh), [searching](../using/search-and-download.md), and [import](../using/search-and-download.md#following-completion). **My Jobs** lets you run eligible jobs, schedule them, or trigger them from events. An [execution](../administration/jobs-and-automation.md#following-an-execution) is one attempt to do the work.

## Following an execution

**My Jobs > History** shows executions your account created. Executions from a schedule or event trigger are not listed. Administrators can see them in [System Jobs](../administration/jobs-and-automation.md#following-an-execution), and the API can [look them up by trigger](../reference/api.md#finding-executions-from-a-personal-trigger).

| Status | Meaning |
| --- | --- |
| Pending or queued | The request exists but has not started, and repeating it duplicates the work. |
| Running | The work is in progress. |
| Completed | This execution finished. The result shows on the title, file, or service. |
| Failed | This attempt did not finish. Its error and the related attempts give the reason. |
| Cancelled | The execution stopped. Changes it already made stay. |

Clear **Show top level only** to see child work.

## Running permitted work

**Run a job** lists the jobs your [role](../reference/roles.md#job-permissions) may run on demand.

| Job scope | Minimum role to run it manually |
| --- | --- |
| User | Requester |
| Content | Manager |
| Administrative or System | Admin |

For example, a Manager can run **Content metadata refresh item** for one movie by entering the movie's identifier in the `contentHierarchyItemId` parameter. The identifier is the last one in the `/library/<content-id>` URL of the movie.

## Adding a personal schedule

Personal schedules need Requester access or higher, and the job must allow them. Under **My Jobs > Scheduled Jobs**, select **Add Schedule**, name the rule, pick the job, fill in its parameters, and set a cron expression. For a daily refresh of one movie, pick **Content metadata refresh item**, set `contentHierarchyItemId` to the movie's identifier, and use `0 5 * * *`.

### Schedule timing

A cron expression has five fields: minute, hour, day of month, month, and weekday. Schedules use UTC, so the example runs at 05:00 UTC.

## Adding an event trigger

An event trigger requests a job when a matching [event](../administration/jobs-and-automation.md#jobs-and-executions) occurs, if the job allows personal event triggers. Without [context filters](../administration/notifications.md#matching-event-context), every matching event triggers it.

Do not trigger a job from an event it emits. A metadata refresh on every metadata-refreshed event would repeat forever.

This example refreshes one movie after a download for it is imported.

1. Open **Event Triggers** and select **Add Event Trigger**. Name the rule.
2. Under **Job Template**, select **Content metadata refresh item** and set `contentHierarchyItemId` to the movie's identifier.
3. Select the **File imported** preset, which matches `download.imported`.
4. Add a context filter with **Key** `content.hierarchyItemId` and **Pattern (regex)** `^<movie id>$`, replacing `<movie id>` with the same identifier. Leave **Negate** off.
5. Leave **Delay (seconds)** at `0`, then enable and save.

The [regular expression](../administration/notifications.md#creating-a-narrow-rule) must match the whole identifier. To find the keys and values for other events, see [event-context inspection](../administration/notifications.md#matching-event-context).

## Investigating failures or stopping work

A failed execution's message and related attempts show the cause. A [retry](../administration/jobs-and-automation.md#configuring-retries) may already be pending. [Troubleshooting](../administration/troubleshooting.md) covers import failures.

Cancelling needs Manager access or higher. It does not undo completed changes. Administrators supervise all schedules, triggers, and executions in [System Jobs](../administration/jobs-and-automation.md).

---

[Power-user guide](README.md) | [Documentation](../README.md)
