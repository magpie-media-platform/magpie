namespace Magpie.CommandLine.Dispatch;

internal sealed record CommandTransportOptions(
    string? Url = null,
    string? ApiKey = null,
    bool IsUrlExplicit = false,
    bool IsApiKeyExplicit = false);
