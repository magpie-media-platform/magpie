using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using AngleSharp.Dom;
using AngleSharp.Html.Parser;

using Magpie.Extensions.Cardigann.Engine.Filters;
using Magpie.Extensions.Cardigann.Engine.Templating;
using Magpie.Models.Data;

using Microsoft.Extensions.Logging;

using MonoTorrent;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class ResultParser(TimeProvider timeProvider, ILogger<ResultParser> logger) : IResultParser
{
    private static readonly Dictionary<string, object?> EmptyVars = new();

    private static readonly string[] DefaultMagnetTrackers =
    [
        "udp://tracker.opentrackr.org:1337/announce",
        "udp://open.tracker.cl:1337/announce",
        "udp://open.stealth.si:80/announce",
        "udp://tracker.torrent.eu.org:451/announce",
        "udp://exodus.desync.com:6969/announce",
        "udp://tracker.openbittorrent.com:6969/announce",
        "udp://explodie.org:6969/announce",
        "udp://tracker.moeking.me:6969/announce",
        "udp://opentracker.i2p.rocks:6969/announce",
        "udp://tracker.dler.org:6969/announce",
    ];

    private static readonly (string Suffix, long Bytes)[] SizeUnits =
    [
        ("PIB", 1L << 50),
        ("TIB", 1L << 40),
        ("GIB", 1L << 30),
        ("MIB", 1L << 20),
        ("KIB", 1L << 10),
        ("PB", 1L << 50),
        ("TB", 1L << 40),
        ("GB", 1L << 30),
        ("MB", 1L << 20),
        ("KB", 1L << 10),
        ("B", 1L),
    ];

    private const string BtihAuthority = "btih";

    private static readonly HashSet<string> UrlValuedKeys = new(StringComparer.Ordinal)
    {
        "poster",
        "banner",
        "image",
        "cover",
        "thumbnail",
        "thumb",
        "icon",
        "screenshot",
        "preview",
        "logo",
    };

    private readonly HtmlParser _htmlParser = new();

    private static string? Get(Dictionary<string, string?> values, string key)
    {
        return values.TryGetValue(key, out var v) ? v : null;
    }

    private static string BuildMagnet(string hash, string? title)
    {
        var magnet = "magnet:?xt=urn:btih:" + hash.Trim();
        if (!string.IsNullOrWhiteSpace(title))
        {
            magnet += "&dn=" + Uri.EscapeDataString(title!);
        }

        foreach (var tracker in DefaultMagnetTrackers)
        {
            magnet += "&tr=" + Uri.EscapeDataString(tracker);
        }

        return magnet;
    }

    private static string? StableMagnetId(Uri uri)
    {
        if (!string.Equals(uri.Scheme, "magnet", StringComparison.OrdinalIgnoreCase) ||
            !MagnetLink.TryParse(uri.ToString(), out var magnet))
        {
            return null;
        }

        return (magnet!.InfoHashes.V1 ?? magnet.InfoHashes.V2)?.ToHex().ToLowerInvariant();
    }

    private static void CaptureIdentityKey(Release release, Uri infoUri, string? infoHash)
    {
        var explicitHash = infoHash?.Trim();
        if (!string.IsNullOrWhiteSpace(explicitHash) && explicitHash.Length is not (32 or 40))
        {
            return;
        }

        var magnetUri = string.IsNullOrWhiteSpace(explicitHash)
            ? infoUri.ToString()
            : "magnet:?xt=urn:btih:" + explicitHash;
        if (!MagnetLink.TryParse(magnetUri, out var magnet) || magnet!.InfoHashes.V1 is not { } v1)
        {
            return;
        }

        var normalizedHash = v1.ToHex().ToLowerInvariant();
        if (normalizedHash.Trim('0').Length == 0)
        {
            return;
        }

        var identityValue = string.IsNullOrWhiteSpace(explicitHash)
            ? normalizedHash
            : explicitHash.ToLowerInvariant();
        if (release.IdentityKeys.Any(identity =>
            string.Equals(identity.Authority, BtihAuthority, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(identity.Value, identityValue, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        release.IdentityKeys.Add(new ReleaseIdentityKey
        {
            Authority = BtihAuthority,
            Value = identityValue,
            Ordinal = release.IdentityKeys.Count,
        });
    }

    private static string? ResolveRawValue(IElement? root, FieldDefinition field)
    {
        if (root is null)
        {
            return field.Text;
        }

        if (field.Text is not null && string.IsNullOrEmpty(field.Selector))
        {
            return field.Text.RenderCardigannTemplate(EmptyVars);
        }

        if (field.Case is not null && field.Case.Count > 0)
        {
            foreach (var (selector, value) in field.Case)
            {
                if (string.Equals(selector, "*", StringComparison.Ordinal) ||
                    (selector.StartsWith(':')
                        ? root.Matches(selector.TrimStart(':'))
                        : root.QuerySelector(selector) is not null))
                {
                    return value;
                }
            }

            return null;
        }

        var element = root;
        if (!string.IsNullOrEmpty(field.Selector) && !string.Equals(field.Selector, ":self", StringComparison.Ordinal))
        {
            element = root.QuerySelector(field.Selector);
        }

        if (element is null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(field.Attribute))
        {
            return element.GetAttribute(field.Attribute);
        }

        return element.TextContent ?? string.Empty;
    }

    private static string MapCategory(IReadOnlyList<CategoryMapping> categoryMappings, string rawValue)
    {
        foreach (var mapping in categoryMappings)
        {
            if (string.Equals(mapping.Id, rawValue, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrEmpty(mapping.Cat) ? rawValue : mapping.Cat;
            }
        }

        return rawValue;
    }

    private static DateTime TryParsePublishDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return default;
        }

        if (raw.Length is >= 9 and <= 11 && long.TryParse(raw, out var epoch))
        {
            return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
        }

        return DateTime.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : default;
    }

    public IReadOnlyList<Release> Parse(
        string html,
        CardigannDefinition definition,
        Uri baseUri,
        IReadOnlyDictionary<string, object?>? variables = null)
    {
        if (string.IsNullOrEmpty(html))
        {
            return [];
        }

        using var document = _htmlParser.ParseDocument(html);
        var rowSelector = (definition.Search.Rows.Selector ?? string.Empty)
            .RenderCardigannTemplate(variables ?? EmptyVars, EscapeSelectorValue)
            .Trim();
        if (string.IsNullOrEmpty(rowSelector))
        {
            logger.LogWarning(
                "Cardigann {Tracker} row selector rendered empty and will return no results",
                definition.Id);
            return [];
        }

        IHtmlCollection<IElement> rows;
        try
        {
            rows = document.QuerySelectorAll(rowSelector);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The parser cannot evaluate Cardigann {Tracker} row selector {Selector} and will return no results",
                definition.Id,
                rowSelector);
            return [];
        }

        var releases = new List<Release>();
        var categoryMappings = definition.ResolveCategoryMappings();

        var rowIndex = 0;
        foreach (var row in rows)
        {
            var release = ExtractRow(row, rowIndex, definition.Search.Fields, baseUri, categoryMappings);
            if (release is not null)
            {
                releases.Add(release);
            }

            rowIndex++;
        }

        return releases;
    }

    public IReadOnlyList<Release> ParseJson(
        string json,
        CardigannDefinition definition,
        Uri baseUri,
        IReadOnlyDictionary<string, object?> variables)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "The Cardigann parser cannot parse the JSON response for {Tracker}", definition.Id);
            return [];
        }

        using (document)
        {
            var rendered = (definition.Search.Rows.Selector ?? string.Empty)
                .RenderCardigannTemplate(variables)
                .Trim();
            var path = rendered.TrimStart('$').Trim();
            if (path.StartsWith(":", StringComparison.Ordinal))
            {
                path = string.Empty;
            }

            var rowsElement = document.RootElement;
            if (path.Length > 0)
            {
                var navigated = document.RootElement.ResolvePath(path);
                if (navigated is null)
                {
                    return [];
                }

                rowsElement = navigated.Value;
            }

            if (rowsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var releases = new List<Release>();
            var categoryMappings = definition.ResolveCategoryMappings();
            foreach (var item in rowsElement.EnumerateArray())
            {
                var release = ExtractJsonRow(item, definition, baseUri, categoryMappings);
                if (release is not null)
                {
                    releases.Add(release);
                }
            }

            return releases;
        }
    }

    public string? ExtractDownloadTarget(
        string html,
        DownloadBlock download,
        IReadOnlyDictionary<string, object?>? variables = null)
    {
        if (string.IsNullOrEmpty(html))
        {
            return null;
        }

        using var document = _htmlParser.ParseDocument(html);
        var vars = variables ?? EmptyVars;

        if (download.InfoHash is not null)
        {
            var hash = download.InfoHash.Hash is null
                ? null
                : RenderSelector(download.InfoHash.Hash, vars) is { } renderedHash
                    ? ExtractField(document.DocumentElement, renderedHash, "download.infohash.hash")
                    : null;
            if (!string.IsNullOrWhiteSpace(hash))
            {
                var title = download.InfoHash.Title is null
                    ? null
                    : RenderSelector(download.InfoHash.Title, vars) is { } renderedTitle
                        ? ExtractField(document.DocumentElement, renderedTitle, "download.infohash.title")
                        : null;
                return BuildMagnet(hash!, title);
            }
        }

        var candidates = new List<FieldDefinition>();
        if (!string.IsNullOrEmpty(download.Selector))
        {
            candidates.Add(new FieldDefinition
            {
                Selector = download.Selector,
                Attribute = download.Attribute,
                Filters = download.Filters,
            });
        }

        candidates.AddRange(download.Selectors);

        foreach (var candidate in candidates)
        {
            if (RenderSelector(candidate, vars) is not { } renderedCandidate)
            {
                continue;
            }

            var value = ExtractField(
                document.DocumentElement,
                renderedCandidate,
                "download.selector");
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string EscapeSelectorValue(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '-' or '_' || !char.IsAscii(character))
            {
                builder.Append(character);
            }
            else if (char.IsControl(character))
            {
                builder.Append(CultureInfo.InvariantCulture, $"\\{(int)character:x} ");
            }
            else
            {
                builder.Append('\\').Append(character);
            }
        }

        return builder.ToString();
    }

    private static FieldDefinition? RenderSelector(
        FieldDefinition field,
        IReadOnlyDictionary<string, object?> variables)
    {
        if (field.Selector is not { Length: > 0 } selector ||
            !selector.Contains("{{", StringComparison.Ordinal))
        {
            return field;
        }

        var rendered = selector.RenderCardigannTemplate(variables, EscapeSelectorValue);
        if (string.IsNullOrWhiteSpace(rendered))
        {
            return null;
        }

        return new FieldDefinition
        {
            Selector = rendered,
            Attribute = field.Attribute,
            Text = field.Text,
            Case = field.Case,
            Optional = field.Optional,
            Default = field.Default,
            Filters = field.Filters,
        };
    }

    private Release? ExtractRow(
        IElement row,
        int rowIndex,
        IReadOnlyDictionary<string, FieldDefinition> fields,
        Uri baseUri,
        IReadOnlyList<CategoryMapping> categoryMappings)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (fieldName, field) in fields)
        {
            if (ExtractField(row, field, $"row[{rowIndex}].{fieldName}") is { } value)
            {
                values[fieldName] = value;
            }
        }

        var title = Get(values, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            logger.LogDebug("Cardigann skipped row {RowIndex} that has no title", rowIndex);
            return null;
        }

        var magnet = Get(values, "magnet");
        var download = Get(values, "download");
        var details = Get(values, "details") ?? Get(values, "comments") ?? Get(values, "guid");
        var link = !string.IsNullOrWhiteSpace(magnet)
            ? magnet
            : !string.IsNullOrWhiteSpace(download)
                ? download
                : details;

        if (string.IsNullOrWhiteSpace(link))
        {
            logger.LogDebug("Cardigann skipped row {RowIndex} that has no download/magnet/details link", rowIndex);
            return null;
        }

        var infoUri = ResolveLink(link!, baseUri, rowIndex);
        if (infoUri is null)
        {
            return null;
        }

        return BuildRelease(values, title!, infoUri, infoHash: null, baseUri, categoryMappings);
    }

    private Release BuildRelease(
        Dictionary<string, string?> values,
        string title,
        Uri infoUri,
        string? infoHash,
        Uri baseUri,
        IReadOnlyList<CategoryMapping> categoryMappings)
    {
        long? size = null;
        var sizeText = Get(values, "size");
        if (!string.IsNullOrWhiteSpace(sizeText))
        {
            size = ParseSize(sizeText);
        }

        var categories = new List<string>();
        var rawCategory = Get(values, "category") ?? Get(values, "cat");
        if (!string.IsNullOrWhiteSpace(rawCategory))
        {
            categories.Add(MapCategory(categoryMappings, rawCategory!));
        }

        var release = new Release
        {
            Title = title.Trim(),
            Protocol = "torrent",
            InfoUrl = infoUri,
            ExternalId = string.IsNullOrWhiteSpace(infoHash)
                ? StableMagnetId(infoUri) ?? infoUri.ToString()
                : infoHash.Trim().ToLowerInvariant(),
            Size = size,
            PublishDate = TryParsePublishDate(Get(values, "date")),
            Categories = categories,
            Properties = SerializeRetainedProperties(values, baseUri),
        };
        CaptureIdentityKey(release, infoUri, infoHash);
        release.CaptureProviderIds(values);
        return release;
    }

    private Uri? ResolveLink(string link, Uri baseUri, int rowIndex)
    {
        if (link.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(link, UriKind.Absolute, out var magnetUri))
        {
            return magnetUri;
        }

        if (Uri.TryCreate(baseUri, link, out var resolved))
        {
            return resolved;
        }

        logger.LogDebug(
            "Cardigann row {RowIndex} link {Link} did not resolve against {BaseUri}",
            rowIndex,
            link,
            baseUri);
        return null;
    }

    public string? ExtractField(IElement? root, FieldDefinition field, string context)
    {
        string? raw;
        try
        {
            raw = ResolveRawValue(root, field);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The parser cannot evaluate the Cardigann selector {Selector} of {Context}",
                field.Selector,
                context);
            return null;
        }

        if (raw is null)
        {
            if (!string.IsNullOrEmpty(field.Default))
            {
                raw = field.Default;
            }
            else if (field.Optional || field.Text is not null || field.Case is not null)
            {
                raw = string.Empty;
            }
            else
            {
                return null;
            }
        }

        return ApplyFilters(raw, field, context);
    }

    private string ApplyFilters(string raw, FieldDefinition field, string context)
    {
        var current = raw.Trim();
        if (field.Filters is null)
        {
            return current;
        }

        foreach (var step in field.Filters)
        {
            try
            {
                current = current.ApplyCardigannFilter(step.Name, step.Args, timeProvider);
            }
            catch (RegexMatchTimeoutException ex)
            {
                logger.LogWarning(
                    ex,
                    "Cardigann filter {Filter} timed out at {Context} and skipped the field (possible ReDoS).",
                    step.Name,
                    context);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Cardigann filter {Filter} failed at {Context}; the value stayed unchanged.",
                    step.Name,
                    context);
            }
        }

        return current;
    }

    private Release? ExtractJsonRow(
        JsonElement item,
        CardigannDefinition definition,
        Uri baseUri,
        IReadOnlyList<CategoryMapping> categoryMappings)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (fieldName, field) in definition.Search.Fields)
        {
            values[fieldName] = ExtractJsonField(item, field, $"json.{fieldName}");
        }

        var title = Get(values, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var magnet = Get(values, "magnet");
        var download = Get(values, "download");
        var infohash = Get(values, "infohash");

        Uri? infoUri = null;
        if (!string.IsNullOrWhiteSpace(magnet))
        {
            infoUri = ResolveLink(magnet!, baseUri, 0);
        }
        else if (!string.IsNullOrWhiteSpace(download))
        {
            infoUri = ResolveLink(download!, baseUri, 0);
        }
        else if (!string.IsNullOrWhiteSpace(infohash) && infohash!.Trim('0').Length > 0)
        {
            Uri.TryCreate(BuildMagnet(infohash!, title), UriKind.Absolute, out infoUri);
        }

        if (infoUri is null)
        {
            return null;
        }

        return BuildRelease(values, title!, infoUri, infohash, baseUri, categoryMappings);
    }

    public void ApplyResolvedDownload(Release release, Uri resolved)
    {
        release.InfoUrl = resolved;
        release.ExternalId = StableMagnetId(resolved) ?? resolved.ToString();
        CaptureIdentityKey(release, resolved, infoHash: null);
    }

    private static long? ParseSize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim().ToUpperInvariant();
        foreach (var (suffix, bytes) in SizeUnits)
        {
            if (trimmed.EndsWith(suffix, StringComparison.Ordinal))
            {
                var numericPart = trimmed.Substring(0, trimmed.Length - suffix.Length).Trim();
                return double.TryParse(numericPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    ? (long)Math.Round(value * bytes)
                    : null;
            }
        }

        return long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bareBytes)
            ? bareBytes
            : null;
    }

    private static string? AbsolutizeUrlValue(string? value, Uri baseUri, bool urlValuedKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value!.Trim();
        if (!trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out _))
            {
                return null;
            }

            if (!urlValuedKey && !trimmed.StartsWith('/'))
            {
                return null;
            }
        }

        return Uri.TryCreate(baseUri, trimmed, out var resolved) ? resolved.ToString() : null;
    }

    private string SerializeRetainedProperties(IReadOnlyDictionary<string, string?> values, Uri baseUri)
    {
        var retained = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            var normalizedKey = pair.Key
                .Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();
            var urlSuffixed = normalizedKey.EndsWith("url", StringComparison.Ordinal);
            if (urlSuffixed)
            {
                normalizedKey = normalizedKey.Substring(0, normalizedKey.Length - 3);
            }

            if (normalizedKey is "magnet" or "download" or "details" or "comments" or "guid" or "link")
            {
                retained.Remove(pair.Key);
                continue;
            }

            var absolute = AbsolutizeUrlValue(
                pair.Value,
                baseUri,
                urlSuffixed || UrlValuedKeys.Contains(normalizedKey));
            if (absolute is not null)
            {
                retained[pair.Key] = absolute;
            }
        }

        return JsonSerializer.Serialize(retained);
    }

    private string? ExtractJsonField(JsonElement item, FieldDefinition field, string context)
    {
        string? raw = null;
        if (field.Text is not null && string.IsNullOrEmpty(field.Selector))
        {
            raw = field.Text.RenderCardigannTemplate(EmptyVars);
        }
        else if (!string.IsNullOrEmpty(field.Selector))
        {
            var element = item.ResolvePath(field.Selector);
            raw = element is null ? null : element.Value.ToScalarString();
        }

        if (raw is null)
        {
            if (!string.IsNullOrEmpty(field.Default))
            {
                raw = field.Default;
            }
            else if (field.Optional)
            {
                raw = string.Empty;
            }
            else
            {
                return null;
            }
        }

        return ApplyFilters(raw, field, context);
    }
}
