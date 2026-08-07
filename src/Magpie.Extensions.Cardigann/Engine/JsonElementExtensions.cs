using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Magpie.Extensions.Cardigann.Engine;

internal static class JsonElementExtensions
{
    public static string? ToScalarString(this JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };
    }

    public static JsonElement? ResolvePath(this JsonElement root, string path)
    {
        var current = root;
        foreach (var segment in Tokenize(path))
        {
            if (segment.Length == 0 || segment == "$")
            {
                continue;
            }

            if (int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                if (current.ValueKind != JsonValueKind.Array || index < 0 || index >= current.GetArrayLength())
                {
                    return null;
                }

                current = current[index];
                continue;
            }

            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private static IEnumerable<string> Tokenize(string path)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < path.Length; i++)
        {
            var c = path[i];
            if (c == '.')
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
            }
            else if (c == '[')
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }

                var end = path.IndexOf(']', i);
                if (end < 0)
                {
                    break;
                }

                var inner = path.Substring(i + 1, end - i - 1).Trim().Trim('\'', '"');
                yield return inner;
                i = end;
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }
}
