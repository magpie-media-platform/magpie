using System;

namespace Magpie.Extensions.Qbittorrent;

internal sealed class QbittorrentAddRejectedException(string message, string responseBody) : Exception(message)
{
    public string ResponseBody { get; } = responseBody;

    public bool IsFailsBody => string.Equals(ResponseBody, "Fails.", StringComparison.Ordinal);
}
