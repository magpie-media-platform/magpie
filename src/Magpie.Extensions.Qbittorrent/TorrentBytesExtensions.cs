using System.IO;

using MonoTorrent;

namespace Magpie.Extensions.Qbittorrent;

internal static class TorrentBytesExtensions
{
    internal static string ComputeInfoHash(this byte[] torrentBytes)
    {
        if (!Torrent.TryLoad(torrentBytes, out var torrent) || torrent.InfoHashes.V1 is not { } hash)
        {
            throw new InvalidDataException("Torrent file has no valid v1 info hash.");
        }

        return hash.ToHex().ToLowerInvariant();
    }
}
