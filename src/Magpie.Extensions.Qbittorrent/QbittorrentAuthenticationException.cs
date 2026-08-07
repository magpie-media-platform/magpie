using System;

namespace Magpie.Extensions.Qbittorrent;

internal sealed class QbittorrentAuthenticationException(string message) : Exception(message)
{
}
