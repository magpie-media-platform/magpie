using Magpie.Models.Enums;

namespace Magpie.Extensions.Qbittorrent;

internal readonly record struct MappingResult(DownloadStatus Status, string? StatusReason);
