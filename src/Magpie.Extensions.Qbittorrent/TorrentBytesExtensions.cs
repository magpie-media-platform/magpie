using System.IO;

using MonoTorrent;

namespace Magpie.Extensions.Qbittorrent;

internal static class TorrentBytesExtensions
{
    internal static string? ComputeInfoHash(this byte[] torrentBytes)
    {
        if (!Torrent.TryLoad(torrentBytes, out var torrent))
        {
            throw new InvalidDataException("The bytes are not a valid .torrent file.");
        }

        return torrent.InfoHashes.V1?.ToHex().ToLowerInvariant();
    }
}
