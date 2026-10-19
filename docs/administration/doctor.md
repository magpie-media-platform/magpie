# Magpie Doctor

[Documentation](../README.md) / [Administration](README.md)

Doctor scans the configuration and data for known problems, such as a root folder that cannot be reached or an extension that failed to load. Each finding names the condition and the affected item, and some offer a repair. The [diagnostic reference](../reference/diagnostics.md) defines the contracts.

A scan runs daily at 05:00 UTC. Doctor is on **Admin > System Status** under **Magpie Doctor**, which shows **Last scanned** and a **Run now** button. Through the API, Viewer and higher roles can read findings with `GET /api/diagnostics/findings`, and repairs require Admin.

## Running and interpreting a scan

**Run now** starts a scan, and **Doctor scan complete** appears when it finishes. **Never** under **Last scanned** means no scan has completed, so **No issues found.** with no scan time says nothing about health.

A clean scan means only that the checks that ran found nothing. A known problem without a finding still needs the logs.

## Built-in checks

| Area | Example findings |
| --- | --- |
| Setup | No indexers, download clients, or root folders are configured. |
| Content defaults | A content type has no default root folder, or a root folder has a missing or stale quality default. |
| Paths | A root folder is not reachable, or a remote path mapping does not resolve. |
| Quality | A tier list has no tiers. |
| Library | Two titles share an external identifier. |
| Extensions | An extension failed to load. |
| Jobs | A built-in schedule or event trigger is missing, disabled, or has invalid timing. |
| Nodes | A cluster node is pending restart or takes no jobs. |

Extensions can add their own checks.

## Investigating a finding

A finding describes a symptom, and the cause is often outside Magpie. "Root folder NAME is not reachable" can mean a missing container mount or a service account without access. In a [cluster](clustering.md), a scan checks only the node that ran it.

Findings about setup and services point to [Storage and services](storage-and-services.md). Extension findings point to [Extensions](extensions.md), metadata findings to [Metadata providers](metadata.md), root folders and remote paths to [Troubleshooting](troubleshooting.md#download-completes-but-import-does-not), and job schedules to [Jobs and automation](jobs-and-automation.md).

## Applying and verifying a repair

Some findings have a **Fix** button. It opens **Apply repair?**, and confirming runs the repair. Run another scan afterwards to confirm the condition is gone. A [recovery set](maintenance.md#preparing-a-recovery-set) made first keeps the change reversible.

---

[Administration](README.md) | [Documentation](../README.md)
