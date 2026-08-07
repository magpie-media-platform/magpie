using System;
using System.Collections.Generic;
using System.Text.Json;

using Magpie.Models.Data;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Jellyfin;

internal static class SettingsExtensions
{
    public static List<Dictionary<string, string>> ToJellyfinMappingRows(
        this MediaServer server,
        ILogger logger)
    {
        if (!server.Settings.TryGetValue("libraryMappings", out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var rows = new List<Dictionary<string, string>>(document.RootElement.GetArrayLength());
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in element.EnumerateObject())
                {
                    row[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Number => property.Value.GetRawText(),
                        _ => string.Empty,
                    };
                }

                rows.Add(row);
            }

            return rows;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "The Jellyfin extension failed to parse the library mappings JSON for server {ServerId}. " +
                "The extension discarded all library mappings for this server.",
                server.Id);
            return [];
        }
    }

    public static JellyfinSettings ToJellyfinSettings(this IReadOnlyDictionary<string, string> settings)
    {
        if (!settings.TryGetValueIgnoringCase(JellyfinSettings.HostKey, out var rawHost) ||
            string.IsNullOrWhiteSpace(rawHost))
        {
            throw new ArgumentException(
                $"Jellyfin setting {JellyfinSettings.HostKey} is required (e.g. 'http://localhost:8096').",
                nameof(settings));
        }

        if (!Uri.TryCreate(rawHost, UriKind.Absolute, out var hostUri) ||
            !(hostUri.Scheme is "http" or "https"))
        {
            throw new ArgumentException(
                $"Jellyfin setting {JellyfinSettings.HostKey} must be an absolute http(s) URL: {rawHost}.",
                nameof(settings));
        }

        if (!hostUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal))
        {
            hostUri = new Uri(hostUri.AbsoluteUri + "/", UriKind.Absolute);
        }

        if (!settings.TryGetValueIgnoringCase(JellyfinSettings.ApiKeyKey, out var apiKey) ||
            string.IsNullOrEmpty(apiKey))
        {
            throw new ArgumentException(
                $"Jellyfin setting {JellyfinSettings.ApiKeyKey} is required (created in Jellyfin admin " +
                "-> Dashboard -> API Keys).",
                nameof(settings));
        }

        return new JellyfinSettings(hostUri, apiKey);
    }

    public static bool TryGetValueIgnoringCase(
        this IReadOnlyDictionary<string, string> settings,
        string key,
        out string value)
    {
        if (settings.TryGetValue(key, out var direct))
        {
            value = direct ?? string.Empty;
            return direct is not null;
        }

        foreach (var pair in settings)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value ?? string.Empty;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }
}
