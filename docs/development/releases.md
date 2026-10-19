# Building and verifying a release

[Documentation](../README.md) / [Development](README.md)

A Magpie release is a set of application archives, [extension packages](publish-extensions.md#assembling-the-runtime-package), a Windows installer, container images, and a Helm chart. They start with different [sets of extensions](#whats-in-each-distribution), so verifying a release means testing the packaged application with its extensions, not only the source build.

The [CI workflow](../../.github/workflows/ci.yml) builds and verifies these artifacts. Once the required jobs pass, a push to `develop` publishes a development prerelease and a release-tag push publishes the tagged version.

## Versions and publishing

A release tag looks like `v1.2.3`, with an optional prerelease suffix such as `v1.2.3-rc.1`. The workflow strips the leading `v` and applies the rest as the version. A prerelease does not update the `latest` image tags. The tagged commit must be in `main`, which the publish job checks after all build jobs. To start a release, tag a commit in `main` and push the tag:

```bash
git tag v1.2.3 <commit>
git push origin v1.2.3
```

A push to `develop` uses the base version of the SDK project and the run number, `<major>.<minor>.<patch>-dev.<run-number>`. It publishes the images and a GitHub prerelease tagged `v<version>` at the pushed commit, but no NuGet packages and no `latest` tags. Only a release-tag push publishes packages to nuget.org. Publication runs only for those pushes in `magpie-media-platform/magpie`. Pull requests, other branches, and manual runs build and verify without publishing.

Before the first release, configure [NuGet trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing#github-actions-setup) on nuget.org. Open the username menu, select **Trusted Publishing**, and add a policy for the owner of the Magpie packages, scoped to the Models, SDK, and released extension packages, with these GitHub values:

| Policy field | Value |
| --- | --- |
| Repository Owner | `magpie-media-platform` |
| Repository | `magpie` |
| Workflow File | `ci.yml` |
| Environment | `release` |

Then add the `NUGET_USER` secret to the repository's `release` environment, set to the nuget.org username of the publishing account. For a tag push the workflow grants `id-token: write` and runs the `NuGet/login` action, which exchanges the GitHub identity token for a temporary API key. Packages are pushed to `https://api.nuget.org/v3/index.json` with `--skip-duplicate`.

The images go to `ghcr.io/magpie-media-platform/magpie`, signed in with `github.actor` and `github.token`. Each tag is a manifest list for `linux/amd64` and `linux/arm64`, and the workflow also pushes a tag per architecture with an `-amd64` or `-arm64` suffix.

| Image | Version tag | Stable release tag |
| --- | --- | --- |
| Standard | `ghcr.io/magpie-media-platform/magpie:<version>` | `latest` |
| Bare | `ghcr.io/magpie-media-platform/magpie:<version>-bare` | `latest-bare` |
| PostgreSQL | `ghcr.io/magpie-media-platform/magpie:<version>-postgresql` | `latest-postgresql` |

The PostgreSQL image is the Standard image plus the PostgreSQL 18 client tools `pg_dump` and `pg_restore`.

The [Helm chart](../installation/helm.md) goes to `oci://ghcr.io/magpie-media-platform/charts`, with its version and `appVersion` set to the release version. Before packaging it, the workflow's `chart` job runs `scripts/verify-chart.sh`, which lints and renders the chart in single-node and cluster mode. GitHub creates a new package as private, so before the first release an organization owner must make the `charts/magpie` package public.

The GitHub release contains the application archives for `win-x64`, `linux-x64`, `linux-arm64`, `osx-arm64`, and `osx-x64`, the `win-x64` MSI, and the chart package `magpie-<version>.tgz`. The macOS archives are built on a macOS runner and the others on Linux. The archives are self-contained and need no separately installed .NET runtime. The image archives stay in the workflow's `image-amd64` and `image-arm64` artifacts and expire with the workflow's artifact retention.

## What's in each distribution

| Distribution | Initial extensions | Contents |
| --- | --- | --- |
| Standalone archive | None | Self-contained application and embedded UI |
| Docker Bare | None | Application, with data and extension directories supplied at deployment |
| Docker Standard | First-party packages selected by the workflow | Application plus those package directories |
| Docker PostgreSQL | Same as Standard | Standard plus the PostgreSQL client tools |
| Windows installer | Chosen during installation | Application plus the selected extension package directories |

The first-party selection is `EXTENSION_PROJECTS` in the workflow. The [first-party extension catalog](../extensions.md) can also list packages published independently of it.

The Standard and PostgreSQL images and the Windows installer install NuGet packages locally, keeping the package metadata and the `lib/<framework>` directory. Application archives do not put extension assemblies in the host's publish output. This layout matters at first start, when the host finds the package directories, loads their entry assemblies, and registers them in the database. See [Extension loading](architecture.md#extension-loading-and-lifetime). The initial selection applies only to a new instance and must not reset the extension choices of an existing installation.

## Building artifacts locally

Building a release requires the [development tools](local-setup.md) and Bash. Verifying artifacts and images also needs Docker, `jq`, and `unzip`, and installer verification runs on Windows. From the repository root, this builds the contracts and one extension, then a Linux archive:

```bash
VERSION=1.0.0
bash scripts/pack-packages.sh "$VERSION" artifacts/packages Magpie.Models Magpie.Sdk Magpie.Extensions.Books
bash scripts/package-app.sh "$VERSION" linux-x64 artifacts/app
```

A distribution release needs the complete `CONTRACT_PROJECTS` and `EXTENSION_PROJECTS` lists from the workflow. The installer and the images take the inputs of the matching workflow job. Images are built from a context where the application archive and the selected NuGet packages are unpacked, not from the host publish directory.

## Verifying package discovery, registration, and activation

A package can have valid ZIP contents and still fail discovery or loading, so check each step.

1. Run `dotnet build src/Magpie.sln -c Release`, `dotnet test src/Magpie.sln -c Release`, and `pnpm run verify-full` in `ui`.
2. Inspect the packaged versions, extension tags, and Magpie dependency ranges, and confirm that every selected extension is present.
3. Inspect the extracted layout. Each extension directory needs a readable `.nuspec` and the matching entry assembly under `lib/<framework>`.
4. Start a fresh instance from the packaged distribution and compare its installed extension list with the selected packages and versions.
5. In the host log, find each extension's load message, check its assembly path, and confirm its dependencies resolve.
6. Test compatible and incompatible [ABI cases](../reference/extensions.md#framework-and-abi-compatibility). A rejected extension must be logged with a useful error and not reported as active.
7. Complete initial setup, run [Doctor](../administration/doctor.md), use a representative configured component, and confirm [readiness](../administration/observability.md#health-and-readiness) and a graceful shutdown.

The [artifact](../../scripts/verify-artifacts.sh), [image](../../scripts/verify-images.sh), and [installer](../../scripts/verify-installer.sh) scripts automate parts of this and run in the workflow jobs.

## Verifying an upgrade and publication

Test an upgrade on an existing instance as well as a fresh install. Before upgrading, pick an extension version, disable an extension, and remove a preset one. Afterward those choices must be unchanged and a new preset package must not alter the installed selection. Also check the database upgrade, retained settings, required keys, storage access, and restart behavior. [Maintenance and recovery](../administration/maintenance.md) lists the state to preserve. Test the Windows service and container startup with the real release artifacts.

After publication, fetch each artifact from its public destination without credentials:

- Download the five application archives and the MSI from the GitHub release and check their version and contents.
- Pull the versioned Standard, Bare, and PostgreSQL images on amd64 and arm64, and check the version and revision labels.
- For a stable release, confirm that `latest`, `latest-bare`, and `latest-postgresql` point to this release.
- Pull the chart with `helm pull oci://ghcr.io/magpie-media-platform/charts/magpie --version <version>`.
- Find a released extension through its NuGet feed, install it, restart, and use it.

An independently published extension goes through the same package, compatibility, upgrade, and runtime checks. See [Publishing an extension](publish-extensions.md).

---

[Development](README.md) | [Documentation](../README.md)
