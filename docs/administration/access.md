# Accounts and access

[Documentation](../README.md) / [Administration](README.md)

Initial setup creates the first administrator. Until it exists, anyone who can reach the setup page can create it, so keep that address private.

## Assigning a role

Each role includes the access of the roles below it. The [role reference](../reference/roles.md) has details.

| Role | Typical task |
| --- | --- |
| Viewer | Read the collection and status. |
| Requester | Add content and use [Interactive Search](../using/search-and-download.md#selecting-a-release). |
| Manager | Maintain content, [metadata](../power-user/metadata.md), and files. |
| Admin | Configure accounts, [services](storage-and-services.md#service-roles), [extensions](extensions.md), and the installation. |

## Creating and maintaining accounts

On **Admin > Users**, **Add User** creates a local account with a **Username**, **Password**, and **Role**. Accounts from an external authentication provider appear at first sign-in, once its extension is configured.

Usernames and providers cannot be changed. Changing a role or enablement ends the account's sessions. Magpie refuses to demote, disable, or delete the last enabled administrator.

## Unlocking an account

A local account locks for 15 minutes after 5 failed sign-ins. An Admin can use **Unlock USERNAME** on **Admin > Users**. `Magpie:Auth:LockoutOptions:MaxFailedAttempts` and `Magpie:Auth:LockoutOptions:LockoutDuration` change the limits.

## Issuing an API key

An API key acts with its owning account's permissions and goes in the `X-Api-Key` header. See [API and command-line use](api-and-cli.md).

On **Admin > Users**, the **API Keys** tab has **Create Key**, with a **Name** and an optional expiry. The key is shown once, so store it right away.

For a script that needs fewer permissions, create an account with a lesser role and have an administrator create its key through the API:

```sh
curl --fail-with-body \
  -H "X-Api-Key: $MAGPIE_API_KEY" \
  -H "Content-Type: application/json" \
  --data '{"name":"collection-report"}' \
  "https://magpie.example.com/api/api-keys?username=media-report"
```

`username` is the owner. The response's `plaintextKey` is the key. To retire a key, disable or delete it.

## Publishing a network address

Outside a trusted local network, put Magpie behind a reverse proxy that terminates HTTPS, and list the proxy's addresses or CIDR ranges in `Magpie:KnownProxies`. A restart applies it.

```json
{ "Magpie": { "KnownProxies": ["192.0.2.10"] } }
```

Live updates use a Server-Sent Events stream at `/api/events/stream`, so the proxy must not buffer it and must keep idle connections open longer than 30 seconds. Restrict the upstream port so clients cannot bypass the proxy.

---

[Administration](README.md) | [Documentation](../README.md)
