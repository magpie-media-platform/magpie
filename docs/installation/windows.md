# Windows service installation

[Documentation](../README.md) / [Installation](README.md)

The Windows installer runs Magpie as a Windows service on 64-bit Windows. After installing, [setup](../quick-start.md#completing-setup) happens at [localhost:8080](http://localhost:8080), followed by the [first download](first-download.md).

## Installing

Download `magpie-VERSION-win-x64.msi` from the **Assets** of a release on the [Releases page](https://github.com/magpie-media-platform/magpie/releases) and run it as an administrator. The installer lists [extensions](../administration/extensions.md) by package ID. For the first download, keep `Magpie.Extensions.Movies`, `Magpie.Extensions.Tmdb`, `Magpie.Extensions.Cardigann`, `Magpie.Extensions.Qbittorrent`, and `Magpie.Extensions.MediaInfo.FFProbe`.

Magpie is installed in `C:\Program Files\Magpie` and runs as the `Magpie` service. It listens on port `8080`, and until the administrator account exists anyone who can open the page can create it. See [Access](../administration/access.md).

## Giving the service access to media

The service runs as `NT SERVICE\Magpie`. It needs read access to completed downloads and Modify access to the library. The [root folder](../administration/storage-and-services.md#adding-a-root-folder) is a Windows path such as `C:\Media\movies`.

For a local folder, open **Properties > Security > Edit > Add**, choose the local computer as the location, and enter `NT SERVICE\Magpie`.

Network storage uses a UNC path such as `\\media-server\media\movies`. Over the network the service acts as the computer account `DOMAIN\COMPUTERNAME$`, which needs share and file permissions on the storage server. For a workgroup server, set an account the server knows under **Services > Magpie > Log On** and give it access to the `config`, `data`, and `extensions` directories.

---

[Installation](README.md) | [Documentation](../README.md)
