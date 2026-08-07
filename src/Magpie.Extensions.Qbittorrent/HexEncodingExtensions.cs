namespace Magpie.Extensions.Qbittorrent;

internal static class HexEncodingExtensions
{
    public static string ToLowerHex(this byte[] bytes)
    {
        var hex = new char[bytes.Length * 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var value = bytes[i];
            hex[i * 2] = ToHexDigit(value >> 4);
            hex[(i * 2) + 1] = ToHexDigit(value & 0x0F);
        }

        return new string(hex);
    }

    private static char ToHexDigit(int value) => (char)(value < 10 ? '0' + value : 'a' + (value - 10));
}
