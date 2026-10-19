# Roles and ownership

[Documentation](../README.md) / [Reference](README.md)

Magpie has four roles, in order: Viewer, Requester, Manager, and Admin. A higher role includes the access of each lower role. Ownership can add conditions on top of the role, so a visible page or button does not mean every operation on it is permitted.

| Operation | Minimum role or boundary |
| --- | --- |
| Read the collection | Viewer. |
| Read [Doctor findings](../administration/doctor.md) | Viewer, through the API. The **System Status** page in administration requires Admin. |
| Create and manage own [API keys](../administration/access.md#issuing-an-api-key) | Viewer. A list of all keys requires Admin. |
| Add content to the collection | Requester. |
| Run [Interactive Search](../using/search-and-download.md#selecting-a-release) and grab one of its results | Requester. |
| Create, change, or delete own [wishlists](../using/monitoring-and-wishlists.md#creating-and-verifying-a-wishlist) | Requester. |
| Create personal schedules and event triggers | Requester. The job's declared capabilities also apply. See [job permissions](#job-permissions). |
| Pause or resume a supported download | Requester. |
| Edit [metadata](../power-user/metadata.md), manage [files](../using/files.md), remove library content | Manager. |
| Import existing files | Manager. |
| Forget or remove a download | Manager. |
| Request [job](../administration/jobs-and-automation.md#jobs-and-executions) cancellation | Manager. |
| Read or test a saved [notification rule](../administration/notifications.md) through the API | Requester. Own rules only, or Admin for any rule. |
| Create, edit, or delete a notification rule through the API | Manager. Ownership still applies. |
| Read settings groups | Manager. |
| Change settings groups | Admin. |
| Configure indexers, download clients, media servers, root folders, remote path mappings, quality tiers, or users | Admin. |
| Open **Admin > Notifications** | Admin. |
| Change [extension](../administration/extensions.md) settings or packages | Admin. |
| Apply a Doctor repair | Admin. |
| Manage [database backups and restore](../administration/maintenance.md#creating-and-retaining-a-database-backup) | Admin. |

## Ownership checks

Role checks do not remove ownership restrictions. An API request for an owned item the account cannot access, such as the wishlist, filter, notification rule, or API key of another account, returns HTTP 404. Manager access, for example, does not make another account's private [saved filter](../power-user/filters-and-magpieql.md#saving-and-reusing-the-selection) editable.

Accounts manage their own API keys, and administrators can manage the keys of other accounts. A key uses the current permissions of its owner.

An owner lock prevents an administrator from changing another account's wishlist. A shared wishlist has no owner, and only an Admin can change or delete it.

For notification rules, see the [notification API](api.md#notification-rules).

### Saved filter ownership

Viewer or higher can save a personal content filter with **Save Filter** on the library page, or through the content filter API. **Add Filter** on the **Filters** page requires Requester. A new filter belongs to the account that creates it, and no route converts a personal filter to a shared one or changes its owner.

A shared filter comes from the wishlist API: with Requester access or higher, a wishlist create or update request with criteria and `saveAsReusableFilter: true` creates a shared copy. The web wishlist form does not have this option. See [Creating a shared filter from a wishlist](api.md#creating-a-shared-filter-from-a-wishlist).

A shared filter has no owner and all accounts can see it. Only an Admin can edit or delete it, including the account that created it.

| Filter | Who can edit or delete it |
| --- | --- |
| Personal filter without an owner lock | Its owner or an Admin. |
| Personal filter with an owner lock | Its owner. The lock blocks other administrators. |
| Existing shared filter | An Admin. |

On a personal filter card, only the owner has the **Lock** and **Unlock** actions. A filter attached to a wishlist has no **Delete**, **Lock**, or **Unlock** action. Its edit button is disabled, with the tooltip **Edit this filter from its wishlist**.

**Clone** creates an independent personal filter for the account that clones it and requires Requester or higher. A Viewer can copy and save the expression instead. See [reuse criteria with another account](../power-user/filters-and-magpieql.md#reusing-criteria-with-another-account).

## Job permissions

A manual run of a User-scope job requires Requester. A Content-scope job requires Manager. Administrative and System scopes require Admin. The job must also permit manual execution: its specification has `onDemand: true`, and `supportedTriggerModes` contains `Manual`.

Interactive Search is a separate operation that requires Requester. The **Search releases** action of a title, and grabs from its cached **Releases** tab, use Content-scope jobs and require Manager.

A job's scope controls manual execution, not personal triggers. The job's declared capabilities control which personal schedule or event triggers a Requester can create, so a job can allow a personal trigger and still require a higher role for immediate execution. No account, including an Admin, can create, change, or delete a trigger for a System-scope job.

A cancellation request does not stop the job immediately. The job stops through its own cancellation behavior.

See [Access](../administration/access.md) and [personal automation](../power-user/personal-automation.md).

---

[Reference](README.md) | [Documentation](../README.md)
