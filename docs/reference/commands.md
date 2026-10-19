# Command reference

[Documentation](../README.md) / [Reference](README.md)

The `magpie` executable has host commands, which start or inspect the process, and application commands, which call API operations such as reading content or queuing a [job](../administration/jobs-and-automation.md#jobs-and-executions). The examples use Linux syntax (`./magpie`). On Windows the executable is `.\magpie.exe`.

Local commands use the data directory of the installation directory they start in. A command started elsewhere uses a different data directory.

**On this page**

- [Host commands](#host-commands)
- [Application commands](#application-commands)
- [Transport and input](#transport-and-input)
- [Interpreting the result](#interpreting-the-result)

## Host commands

| Command | Purpose |
| --- | --- |
| `./magpie version` | Print the application version. |
| `./magpie sentinel` | Run the host with restart supervision. |
| `./magpie run` | Run the host directly. |
| `./magpie --help` | List the host commands and the application command groups. |

Under systemd or as a Windows service, use `run`. Otherwise use `sentinel`. See [Standalone installation](../installation/standalone.md).

## Application commands

Application commands are grouped by API area: `api-keys`, `content`, `content-search`, `database`, `diagnostics`, `download-clients`, `event-triggers`, `extensions`, `indexers`, `jobs`, `library-import`, `media-servers`, `notification-rules`, `quality-tier-lists`, `quality-tiers`, `root-folders`, `settings`, and `users`.

Other API areas, such as wishlists, schedule triggers, saved filters, downloads, Interactive Search, subjects, tags, and remote path mappings, have no command. Use the [HTTP API](api.md) for them. Every group and command has help:

```sh
./magpie jobs --help
./magpie jobs list --help
./magpie content get --help
```

A route value, such as an ID, is a positional argument. A query parameter is an option with a kebab-case name. For example, `pageSize` becomes `--page-size`. Repeat an option to supply several values, such as `--status Failed --status Cancelled`.

Examples of read operations:

| Command | Result |
| --- | --- |
| `jobs list` | Job execution page. |
| `jobs get ID` | One execution. |
| `jobs specs` | Registered job descriptions. |
| `content list` | Collection page. |
| `content get ID` | One content item. |
| `database info` | Database management capabilities. A remote request requires an Admin key. |
| `diagnostics findings` | Current [Doctor findings](../administration/doctor.md). |

`ID` is the record's identifier.

Job write operations use these commands:

| Command | Result |
| --- | --- |
| `jobs enqueue` | Queue an execution. |
| `jobs run` | Run a job immediately when its specification permits it. The command waits until the job ends. |
| `jobs cancel ID` | Request cancellation of an execution. |

The `enqueue` and `run` bodies use the [job request fields](api.md#submitting-work). A remote target applies the [job permissions](roles.md#job-permissions).

## Transport and input

| Option | Use |
| --- | --- |
| `--url URL` | Target a running instance. Requires an [API key](../administration/access.md#issuing-an-api-key). |
| `--api-key KEY` | Supply the remote API key. Without this option, a remote command reads `MAGPIE_API_KEY`. |
| `--json JSON` | Supply a JSON request body. Only commands with a JSON body have this option. |
| `--data PATH` | Read a JSON request body from a file. `@PATH` is also accepted. |
| `--file PATH` | Supply the file for an upload command. Upload commands require this option. |

### Selecting the target explicitly

To target a running instance, supply its base URL and an API key. The base URL is the application address, such as `http://localhost:8080`, without the `/api` suffix. The key's owner determines the permissions. With `MAGPIE_API_KEY` set, this command reads one page of that instance's job executions:

```sh
./magpie jobs list --url http://localhost:8080
```

The [HTTP API reference](api.md#pagination) describes the page fields.

Without `--url`, an application command starts a local in-process host for the installation in the current directory. The local host:

- uses the current directory as its content root, with the local [configuration](../administration/configuration.md#changing-host-settings) and storage;
- applies pending database migrations and loads the installed extensions;
- runs every request as the system account, with Admin permissions;
- does not start the job runtime, so a local `jobs enqueue` only records a queued execution for a running service on the same database to run, while a local `jobs run` runs the job inside the command process.

`--api-key` without `--url` is rejected, and `MAGPIE_API_KEY` is read only when a URL is supplied. Remote URLs must be absolute HTTP or HTTPS addresses, and a remote request without a key is rejected.

### Supplying request data

Use `--data` for a body with several fields or quotes, and `--json` for a short one. The body must match the operation's request contract. A command rejects `--json` and `--data` together.

## Interpreting the result

A successful response goes to standard output with exit code 0. A request failure goes to standard error with exit code 1. In local mode, host log messages also go to standard error.

Exit code 0 means the HTTP request succeeded, not that the job did:

- `jobs run` also returns 0 when the returned `status` is `Failed` or `Cancelled`.
- `jobs enqueue` prints the execution ID as a quoted JSON string. Use it with `jobs get ID` against the same target.
- `jobs cancel ID` requests cancellation, and the work does not stop immediately.

See [API and command-line use](../administration/api-and-cli.md).

---

[Reference](README.md) | [Documentation](../README.md)
