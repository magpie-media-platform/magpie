using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Magpie.Extensions.Qbittorrent;

internal static class TorrentBytesExtensions
{
    private const int MaxNestingDepth = 100;

    internal static string ComputeInfoHash(this byte[] torrentBytes)
    {
        var infoRange = LocateInfoDictRange(torrentBytes);
        using var sha = SHA1.Create();
        var digest = sha.ComputeHash(torrentBytes, infoRange.Start, infoRange.Length);

        return digest.ToLowerHex();
    }

    private static (int Start, int Length) LocateInfoDictRange(byte[] bytes)
    {
        var pos = 0;
        if (pos >= bytes.Length || bytes[pos] != (byte)'d')
        {
            throw new InvalidDataException("Torrent file does not start with a bencoded dictionary.");
        }

        pos++;

        while (pos < bytes.Length && bytes[pos] != (byte)'e')
        {
            var keyStart = pos;
            var keyEnd = ConsumeValue(bytes, pos, 0);
            var keyContent = ExtractStringContent(bytes, keyStart);
            pos = keyEnd;

            var valueStart = pos;
            var valueEnd = ConsumeValue(bytes, pos, 0);
            pos = valueEnd;

            if (keyContent == "info")
            {
                return (valueStart, valueEnd - valueStart);
            }
        }

        throw new InvalidDataException("Torrent file is missing the 'info' dictionary.");
    }

    private static int ConsumeValue(byte[] bytes, int pos, int depth)
    {
        if (depth > MaxNestingDepth)
        {
            throw new InvalidDataException(
                $"Bencoded data nesting exceeds the maximum supported depth of {MaxNestingDepth}.");
        }

        if (pos >= bytes.Length)
        {
            throw new InvalidDataException("Unexpected end of torrent data while parsing.");
        }

        var prefix = bytes[pos];

        if (prefix == (byte)'i')
        {
            var end = IndexOf(bytes, pos + 1, (byte)'e');
            if (end < 0)
            {
                throw new InvalidDataException("Bencoded integer missing terminating 'e'.");
            }

            return end + 1;
        }

        if (prefix is ((byte)'l') or ((byte)'d'))
        {
            var inner = pos + 1;
            while (inner < bytes.Length && bytes[inner] != (byte)'e')
            {
                inner = ConsumeValue(bytes, inner, depth + 1);
            }

            if (inner >= bytes.Length)
            {
                throw new InvalidDataException("Bencoded list/dict missing terminating 'e'.");
            }

            return inner + 1;
        }

        if (prefix is >= ((byte)'0') and <= ((byte)'9'))
        {
            var colon = IndexOf(bytes, pos, (byte)':');
            if (colon < 0)
            {
                throw new InvalidDataException("Bencoded string missing ':' separator.");
            }

            var lengthText = Encoding.ASCII.GetString(bytes, pos, colon - pos);
            if (!int.TryParse(lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) ||
                length < 0)
            {
                throw new InvalidDataException($"Bencoded string has invalid length {lengthText}.");
            }

            var contentStart = colon + 1;
            if (contentStart + length > bytes.Length)
            {
                throw new InvalidDataException("Bencoded string length exceeds remaining buffer.");
            }

            return contentStart + length;
        }

        throw new InvalidDataException(
            $"Unrecognised bencode prefix 0x{prefix:X2} at position {pos}.");
    }

    private static string ExtractStringContent(byte[] bytes, int pos)
    {
        var colon = IndexOf(bytes, pos, (byte)':');
        if (colon < 0)
        {
            throw new InvalidDataException("Bencoded string missing ':' separator.");
        }

        var lengthText = Encoding.ASCII.GetString(bytes, pos, colon - pos);
        if (!int.TryParse(lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) ||
            length < 0)
        {
            throw new InvalidDataException($"Bencoded string has invalid length {lengthText}.");
        }

        var contentStart = colon + 1;
        if (contentStart + length > bytes.Length)
        {
            throw new InvalidDataException("Bencoded string length exceeds remaining buffer.");
        }

        return Encoding.UTF8.GetString(bytes, contentStart, length);
    }

    private static int IndexOf(byte[] bytes, int start, byte value)
    {
        for (var i = start; i < bytes.Length; i++)
        {
            if (bytes[i] == value)
            {
                return i;
            }
        }

        return -1;
    }
}
