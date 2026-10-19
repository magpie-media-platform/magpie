# Standalone installation

[Documentation](../README.md) / [Installation](README.md)

The standalone archive has no installer. Extract it, run it from a terminal, and optionally hand it to a service manager. It contains no [extensions](../administration/extensions.md), so you install them during setup.

## Choosing and extracting an archive

Download the archive for your system from the **Assets** of a release on the [Releases page](https://github.com/magpie-media-platform/magpie/releases):

| System | Archive |
| --- | --- |
| Linux x64 | `magpie-VERSION-linux-x64.tar.gz` |
| Linux ARM64 | `magpie-VERSION-linux-arm64.tar.gz` |
| macOS, Apple silicon | `magpie-VERSION-osx-arm64.tar.gz` |
| macOS, Intel | `magpie-VERSION-osx-x64.tar.gz` |
| Windows x64 | `magpie-VERSION-win-x64.tar.gz` |

The Linux archives need glibc, so they do not run on Alpine. Your distribution must be [supported by .NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md#linux) and have the [native dependencies](https://learn.microsoft.com/en-us/dotnet/core/install/linux-scripted-manual#dependencies) installed.

Extract the archive into a directory of its own, with the executable at the root. The account that runs Magpie must be able to write to the `config`, `data`, and `extensions` directories there. The examples use `/opt/magpie` on Linux and macOS and `C:\Magpie` on Windows.

## Starting in a terminal

On Linux or macOS:

```sh
cd /opt/magpie
chmod +x magpie
./magpie sentinel
```

On Windows, in PowerShell:

```powershell
Set-Location C:\Magpie
.\magpie.exe sentinel
```

Keep the terminal open, and press Ctrl+C to stop. Magpie listens on port 8080 of the local computer only. To reach it from elsewhere, set `ASPNETCORE_URLS` and see [Access](../administration/access.md).

## Installing extensions and completing setup

Open [localhost:8080](http://localhost:8080) and follow [initial setup](../quick-start.md#completing-setup). Install extensions through **Add extension** once the administrator exists. For the [first download](first-download.md), that means Movies, TMDb, Cardigann, qBittorrent, and MediaInfo (FFprobe). See [Install extensions](../administration/install-extensions.md).

MediaInfo (FFprobe) also needs the `ffprobe` executable in the Magpie directory, the current directory, or on `PATH`. The extension does not install it. See the [MediaInfo (FFprobe) instructions](../../src/Magpie.Extensions.MediaInfo.FFProbe/README.md).

Use native paths for storage, such as `/srv/media/movies` for the [root folder](../administration/storage-and-services.md#adding-a-root-folder). The Magpie account needs read access to downloads and write access to the library. If another service reports a different path, add a [remote path mapping](../administration/storage-and-services.md#remote-paths).

## Running continuously with systemd

On Linux with systemd, run `./magpie run` as a service. Stop the terminal instance first, then create a `magpie` system account that can write to `config`, `data`, and `extensions` and reach your media:

```sh
sudo useradd --system --no-create-home --shell /usr/sbin/nologin magpie
sudo chown -R magpie:magpie /opt/magpie/config /opt/magpie/data /opt/magpie/extensions
```

Create `/etc/systemd/system/magpie.service`, adjusting the account, paths, and address to your installation:

```ini
[Unit]
Description=Magpie
After=network.target

[Service]
Type=notify
User=magpie
Group=magpie
WorkingDirectory=/opt/magpie
ExecStart=/opt/magpie/magpie run
Environment=ASPNETCORE_URLS=http://localhost:8080
Restart=on-failure
RestartSec=5
RestartPreventExitStatus=8

[Install]
WantedBy=multi-user.target
```

```sh
sudo systemctl daemon-reload
sudo systemctl enable --now magpie
sudo journalctl -u magpie -n 100 --no-pager
```

If the setup page appears empty after moving to systemd, the working directory differs from the one you used before. Fix that before creating another administrator. On Windows, use the [service installer](windows.md) instead. Under another process supervisor, run `sentinel`.

## Preserving the installation

Magpie keeps its state in three directories relative to the working directory:

| Directory | Contents |
| --- | --- |
| `config` | Host settings, including database selection |
| `data` | The default [SQLite database](../administration/databases.md) and [keys](../administration/maintenance.md#preserving-keys) |
| `extensions` | Installed extension packages |

Terminal runs, services, and upgrades must use the same working directory. Before replacing the archive, stop Magpie and back up these directories and your media, following [Maintenance](../administration/maintenance.md).

---

[Installation](README.md) | [Documentation](../README.md)
