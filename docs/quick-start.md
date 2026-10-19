# Quick start

[Documentation](README.md)

Start Magpie with one of the methods below, then complete initial setup. For other deployment methods, see [Installation](installation/README.md).

## Starting Magpie

### Windows

1. Open the [Releases page](https://github.com/magpie-media-platform/magpie/releases) and download `magpie-VERSION-win-x64.msi` from the **Assets** of a release.
2. Run the installer as an administrator. Keep the default [extension](administration/extensions.md) selection.

The installer starts Magpie as a Windows service on port `8080` of all network interfaces. Until the administrator account exists, any user who can open the page can create it. For details, see [Windows service installation](installation/windows.md).

### Linux or macOS

Download the archive for your system from the [Releases page](https://github.com/magpie-media-platform/magpie/releases): `linux-x64` or `linux-arm64` for Linux, `osx-arm64` for Apple silicon, or `osx-x64` for Intel Macs. The Linux archives require a distribution with glibc. Extract the archive and start Magpie:

```sh
mkdir -p ~/magpie
tar -xzf magpie-VERSION-RID.tar.gz -C ~/magpie
cd ~/magpie
./magpie sentinel
```

Keep the terminal open. To stop Magpie, press Ctrl+C. The archive contains no extensions. Setup installs them. For details, see [Standalone installation](installation/standalone.md).

### Docker

Docker must run Linux containers on AMD64 or ARM64.

```sh
docker run -d --name magpie --restart unless-stopped \
  -p 127.0.0.1:8080:8080 \
  -v magpie-data:/app/data \
  ghcr.io/magpie-media-platform/magpie:latest
```

The [named volume](installation/docker.md#preserving-data-and-connecting-storage) keeps application data when the container is replaced. The Standard image includes the first-party extensions. For details, see [Docker installation](installation/docker.md).

## Completing setup

Open [localhost:8080](http://localhost:8080) on the computer that runs Magpie.

1. **Database:** If this screen appears, keep [**SQLite (local file, default)**](administration/databases.md) and leave the connection string blank. Select **Save and continue**. If **Restart** appears, select it. Otherwise run `docker restart magpie`, then select **I have restarted Magpie**.
2. **Administrator:** Enter the details of the first [administrator account](reference/roles.md) and select **Create administrator**.
3. **Extensions:** The Windows installer and the Docker image include the first-party extensions. Select **Continue**. For a Linux or macOS archive, select **Add extension** and install Movies, TMDb, Cardigann, qBittorrent, and MediaInfo (FFprobe). Select **Restart**, then **Continue**. See [Installing during setup](administration/install-extensions.md#installing-during-setup).
4. On the [service](administration/storage-and-services.md#service-roles) and [root folder](administration/storage-and-services.md#adding-a-root-folder) screens, select **Skip for now**. These screens are metadata, indexer, download client, root folder, and media server.
5. Review the summary and select **Finish setup**. If Magpie asks for a sign-in, sign in.

Magpie then opens its home page for the administrator account. Continue with [Downloading the first movie](installation/first-download.md) to connect services and import a file.

---

[Documentation](README.md)
