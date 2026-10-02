using System;
using System.Collections.Generic;

namespace Magpie.Extensions.Qbittorrent;

internal sealed record QbittorrentSettings
{
    public const string HostKey = "host";

    public const string UsernameKey = "username";

    public const string PasswordKey = "password";

    public const string CategoryKey = "category";

    public const string PausedKey = "paused";

    public const string DefaultCategory = "magpie";

    private QbittorrentSettings(Uri host, string? username, string? password, string category, bool addPaused)
    {
        Host = host;
        Username = username;
        Password = password;
        Category = category;
        AddPaused = addPaused;
    }

    public Uri Host { get; }

    public string? Username { get; }

    public string? Password { get; }

    public string Category { get; }

    public bool AddPaused { get; }

    public bool HasCredentials => !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password);

    public static QbittorrentSettings Parse(IReadOnlyDictionary<string, string> settings)
    {
        if (!TryGet(settings, HostKey, out var rawHost) || string.IsNullOrWhiteSpace(rawHost))
        {
            throw new ArgumentException($"qBittorrent setting {HostKey} is required.", nameof(settings));
        }

        if (!Uri.TryCreate(rawHost, UriKind.Absolute, out var hostUri))
        {
            throw new ArgumentException(
                $"qBittorrent setting {HostKey} is not a valid absolute URL: {rawHost}.",
                nameof(settings));
        }

        if (!hostUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal))
        {
            hostUri = new Uri(hostUri.AbsoluteUri + "/", UriKind.Absolute);
        }

        var username = TryGet(settings, UsernameKey, out var u) && !string.IsNullOrEmpty(u) ? u : null;
        var password = TryGet(settings, PasswordKey, out var p) && !string.IsNullOrEmpty(p) ? p : null;
        var category = TryGet(settings, CategoryKey, out var c) && !string.IsNullOrWhiteSpace(c) ? c : DefaultCategory;
        var addPaused = TryGet(settings, PausedKey, out var paused) && ParseBool(paused);

        return new QbittorrentSettings(hostUri, username, password, category, addPaused);
    }

    private static bool ParseBool(string value)
    {
        if (bool.TryParse(value, out var b))
        {
            return b;
        }

        return value == "1";
    }

    private static bool TryGet(
        IReadOnlyDictionary<string, string> settings,
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
