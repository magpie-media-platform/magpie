using System;

namespace Magpie.Extensions.Jellyfin;

internal sealed record JellyfinSettings
{
    public const string HostKey = "host";

    public const string ApiKeyKey = "apiKey";

    internal JellyfinSettings(Uri host, string apiKey)
    {
        Host = host;
        ApiKey = apiKey;
    }

    public Uri Host { get; }

    public string ApiKey { get; }
}
