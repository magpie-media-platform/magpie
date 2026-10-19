# Media inspectors

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

A media inspector reads technical properties from a file that already exists: container, duration, bit rate, and the video, audio, and subtitle tracks. These are measured values that a [metadata provider](metadata-providers.md) cannot supply from a catalog. You write one to wrap a probe library or executable. Installing the extension does not install an external executable, so document any binary and platform requirements with the package.

## The contract

`IMediaInfoExtractor.ExtractAsync` receives a path visible to the Magpie host and returns a `MediaInfo`, or null when nothing can be extracted. `MediaInfo` is in `Magpie.Models.Data`. `DurationSeconds` is a nullable double in seconds, `BitRate` is a nullable long in bits per second, and `Tracks` is a list of `MediaInfoTrack`. Convert values the source reports in milliseconds or kilobits per second, and leave unavailable properties null instead of guessing.

```csharp
return new MediaInfo
{
    Container = container,
    DurationSeconds = durationSeconds,
    BitRate = bitRate,
    Tracks = tracks
};
```

The [FFprobe extractor](../../../src/Magpie.Extensions.MediaInfo.FFProbe/FFProbeMediaInfoExtractor.cs) is a complete implementation that maps real probe output.

Register the extractor in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point). The interface is in `Magpie.Sdk.Media`.

```csharp
services.AddSingleton<IMediaInfoExtractor, ExampleMediaInfoExtractor>();
```

Use a [singleton](../using-built-in-services.md#service-lifetime-and-ownership) only if the extractor can handle concurrent calls, as FFprobe does. Keep probe state local to each call, pass cancellation to the probe, and don't leave a probe running after a cancelled call.

## Where Magpie calls it

| Consumer | Use of the result |
| --- | --- |
| Download import | Probes each primary source file before placement. The result feeds the rename preview and is saved with the file. |
| **Media info extract scan** and its item job | Saves a non-null result on the [media-file record](../../using/files.md#inspecting-and-correcting-file-associations). The item job also updates `FileKind`, `MimeType`, and the file size from the file system. |
| Media file reconciliation | Compares `DurationSeconds` to accept a recoded file. |

Each of these resolves one extractor, the [last registration](README.md#registration-order). They don't combine extractors.

## Nulls and failures

Return null for input that can't be probed. A null result leaves the stored media information unchanged and does not remove the file. Throw for an unexpected probe failure.

The item [job](../../administration/jobs-and-automation.md#jobs-and-executions) skips a stale or missing file before calling the extractor. When its `requireMissingMediaInfo` parameter is `true`, the default, it also skips a file that already has media information. If the extractor throws, the job logs the failure and fails, and its `Retry` failure policy runs it again. Without an extractor, the job reports that extraction is unavailable. During import, an extractor exception is logged as a warning and the file is imported without media information.

A completed job can therefore leave a record unchanged, so check the job result before assuming the probe ran.

See the [extractor contract](../../../src/Magpie.Sdk/Media/IMediaInfoExtractor.cs), the [media-information model](../../../src/Magpie.Models/Data/MediaInfo.cs), and the [refresh consumer](../../../src/Magpie.Core/Jobs/Metadata/MediaInfoExtractItemJob.cs).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
