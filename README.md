<p align="center">
  <img src="ui/public/logo-transparent.png" alt="Magpie logo" width="128" height="128" />
</p>

<h1 align="center">Magpie</h1>

Magpie is a self-hosted media manager for any media type, including movies, TV shows, music, and books, with the ability to add more through [extensions](docs/administration/extensions.md). It tracks the titles you want and automates finding and downloading them to your media server. [Wishlists](docs/using/monitoring-and-wishlists.md) discover new titles that fit a description, like Christopher Nolan movies.

## Quick start

On Windows, run the installer from the [Releases page](https://github.com/magpie-media-platform/magpie/releases).

On Linux or macOS, download the archive for your system from the same page, extract it, and run:

```sh
./magpie sentinel
```

With Docker:

```sh
docker run -d --name magpie --restart unless-stopped -p 127.0.0.1:8080:8080 -v magpie-data:/app/data ghcr.io/magpie-media-platform/magpie:latest
```

Then open [localhost:8080](http://localhost:8080) and [finish setup](docs/quick-start.md#completing-setup).

## Extensions

Most of Magpie is built from extensions, including media types, download sources, and connections to other services. The first-party extensions use the same APIs as anyone else, so you can [write your own](docs/development/README.md).

## Deployment

Magpie runs on Windows, Linux, and macOS. You can run it from a download on the [Releases page](https://github.com/magpie-media-platform/magpie/releases), or install it as a service on Windows or Linux. Docker images and a Helm chart are also available. The [installation guides](docs/installation/README.md) cover each option, including running Magpie as a multi-node cluster.

Several database backends are supported, with SQLite as the default which requires no additional configuration. PostgreSQL and Microsoft SQL Server can be configured with [additional setup](docs/administration/databases.md).

## Documentation

See the [documentation](docs/README.md) for using, administering, and extending Magpie.

Contributions must follow the [AI Usage Policy](AI_POLICY.md).
