![Magpie](logo.png)

# MediaInfo (FFprobe) extension for Magpie

[Magpie](https://github.com/magpie-media-platform/magpie) is a self-hosted media manager for any media type, including movies, TV shows, music, and books, with the ability to add more through extensions.

The MediaInfo (FFprobe) extension lets Magpie read technical information from media files, including video, audio, and subtitle tracks, using FFprobe.

## Providing FFprobe

The Magpie Docker image already includes `ffprobe`. Otherwise install it in `PATH` or the application directory, where the account running Magpie can find it, and restart Magpie. [Doctor](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/doctor.md) checks that it is found.

## Verifying inspection

[Import a supported media file](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/files.md#importing-existing-files) and compare the reported tracks in its [file details](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/files.md#inspecting-and-correcting-file-associations) with the file.

If no file has information, the process probably cannot find the executable, and the [host log](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/observability.md#reading-host-logs) shows it.
If only one file fails, check its path, read permission, and the reported format error. Other failures show in the [job](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/jobs-and-automation.md#jobs-and-executions) result.

---

[First-party extensions](https://github.com/magpie-media-platform/magpie/blob/main/docs/extensions.md) | [Documentation](https://github.com/magpie-media-platform/magpie/blob/main/docs/README.md)
