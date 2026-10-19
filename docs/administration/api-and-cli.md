# API and command-line use

[Documentation](../README.md) / [Administration](README.md)

The `magpie` command line and the HTTP API do everything the web interface does. The examples use the Linux executable from a [standalone distribution](../installation/standalone.md). On Windows, use `.\magpie.exe`.

## Local commands

Run Magpie commands from your installation directory:

```sh
./magpie jobs list
```

## Targeting a running instance

To run commands against a running Magpie, or to call the API, use an [API key](access.md#issuing-an-api-key). Set it in `MAGPIE_API_KEY` and give the base address of your instance, without an `/api` suffix:

```sh
./magpie jobs list --url https://magpie.example.com
```

With HTTP, send the key in the `X-Api-Key` header:

```sh
curl --fail-with-body \
  -H "X-Api-Key: $MAGPIE_API_KEY" \
  https://magpie.example.com/api/jobs
```

The response is a page of [job executions](jobs-and-automation.md#jobs-and-executions). For more than one page, see [pagination](../reference/api.md#pagination).
## Requesting work and following its result

`jobs specs` lists the jobs that can be requested. `jobs enqueue` queues one, and `jobs run` runs it immediately when the job supports it.

```sh
./magpie jobs specs --url https://magpie.example.com
./magpie jobs enqueue --data doctor-job.json --url https://magpie.example.com
```

Here `doctor-job.json` holds a request for a [Doctor scan](doctor.md):

```json
{
  "jobType": "DiagnosticCheck",
  "parameters": {}
}
```

The command prints the execution GUID, and `jobs get EXECUTION_ID` shows its `status`. For the Doctor scan, `./magpie diagnostics findings` then lists what it found.

The exit code shows whether Magpie accepted the request, not how the job ended.

## Failures

`--help` on any command lists its options, and `--data` reads a JSON body from a file. The [API reference](../reference/api.md#failures-and-diagnostic-reads) defines the results, and the [command reference](../reference/commands.md) has the exact syntax.

---

[Administration](README.md) | [Documentation](../README.md)
