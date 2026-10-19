# Local development

[Documentation](../README.md) / [Development](README.md)

A local development instance is two processes: the [Magpie host](architecture.md) and the Vite UI server. The browser loads the UI from Vite, and the UI calls the host's API directly at the `VITE_API_URL` address `http://localhost:5000/api` set in `ui/.env.development`. UI changes reload while the host keeps its test data. For extension-only work you do not need this setup, only the .NET SDK and a compatible test host. See [Creating an extension](extensions.md).

A development host changes stored settings and runs [background jobs](../administration/jobs-and-automation.md#jobs-and-executions), so it needs its own data and extension directories, plus disposable media paths and test accounts for downloads or imports.

## Preparing the tools and source

Local development requires Git, the .NET 10 SDK, Node.js 22, and pnpm 10.33.0. The [CI workflow](../../.github/workflows/ci.yml) selects the .NET and Node.js versions, and [package.json](../../ui/package.json) selects pnpm. From the repository root:

```powershell
dotnet restore src/Magpie.sln
dotnet build src/Magpie.sln -c Release
cd ui
pnpm install --frozen-lockfile
cd ..
```

## Starting an isolated host

Set the environment, then start the host from the repository root:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:5000"
$env:Magpie__AllowedOrigins__0 = "http://localhost:5173"
$env:Magpie__Data__Path = Join-Path $PWD ".local-data"
$env:Magpie__Extensions__Path = Join-Path $PWD ".local-extensions"
dotnet run --project src/Magpie.App -- run
```

The UI calls the host from another origin, which the host only allows for origins in `Magpie:AllowedOrigins`. The data directory holds the database and data-protection keys, and the extension directory holds installed packages. Do not commit either. Without other configuration the host uses [SQLite](../administration/databases.md). Initial setup writes the database choice to `config/appsettings.json` under the host's working directory, so returning to this instance needs the same environment and working directory. If the host opens an unexpected database or directory, check the [configuration sources](../administration/configuration.md).

To check that the host is ready, run this in a second terminal:

```powershell
(Invoke-WebRequest http://localhost:5000/api/system/ready).StatusCode
```

It returns `200` once the host accepts requests and recognizes its database schema. See [health and readiness](../administration/observability.md#health-and-readiness).

## Starting the UI and completing setup

In another terminal:

```powershell
cd ui
pnpm run dev
```

Open the URL Vite prints, normally `http://localhost:5173`, and complete the [initial setup](../quick-start.md#completing-setup). To try acquisition, continue with [Downloading the first movie](../installation/first-download.md), using paths on this computer. A CORS error in the browser means `Magpie:AllowedOrigins` does not contain the Vite address.

If the host uses another address, set both UI targets before starting Vite:

```powershell
$env:VITE_API_URL = "http://localhost:5001/api"
$env:VITE_API_PROXY_TARGET = "http://localhost:5001"
pnpm run dev
```

`VITE_API_PROXY_TARGET` replaces the default proxy target in the [Vite configuration](../../ui/vite.config.ts).

A source build has no extensions installed. To test a first-party extension from the same checkout, pack it:

```powershell
dotnet pack src/Magpie.Extensions.Movies/Magpie.Extensions.Movies.csproj -c Release -o .local-packages
```

Then upload the package from **Admin > Extensions**, as described in [Installing a local package](../administration/install-extensions.md#installing-a-local-package), and restart the host. You can also extract a package into its own directory under `.local-extensions` with its [NuGet layout](publish-extensions.md#assembling-the-runtime-package), but the host only reads those directories on [first start](architecture.md#extension-loading-and-lifetime).

## Debugging

Start `Magpie.App` from a .NET debugger with the argument `run` and the same environment and working directory, then set a breakpoint where the operation is handled. An installed extension runs inside the host process and the host runs the installed copy of its assembly, so a rebuilt project has no effect until you pack, upload, and restart. Debugging an installed assembly needs its `.pdb` beside it.

## Checks

For backend changes, from the repository root:

```powershell
dotnet build src/Magpie.sln -c Release
dotnet test src/Magpie.sln -c Release
```

For UI changes, `pnpm run verify-full` in `ui` runs the format check, lint, build, and tests. The [repository rules](../../AGENTS.md) cover test boundaries and code conventions.

---

[Development](README.md) | [Documentation](../README.md)
