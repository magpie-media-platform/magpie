using System;
using System.CommandLine;

namespace Magpie.CommandLine.Dispatch;

internal sealed class CommandTransportOptionSet
{
    private readonly Option<string> _urlOption = new("--url")
    {
        Description = "Remote Magpie base URL. Omit to run locally through the in-process host."
    };

    private readonly Option<string> _apiKeyOption = new("--api-key")
    {
        Description = "API key for remote commands. Defaults to MAGPIE_API_KEY."
    };

    private static bool WasExplicit(ParseResult parseResult, Option option)
    {
        var result = parseResult.GetResult(option);

        return result is { Implicit: false };
    }

    public void AddTo(Command command)
    {
        command.Options.Add(_urlOption);
        command.Options.Add(_apiKeyOption);
    }

    public CommandTransportOptions Bind(ParseResult parseResult)
    {
        var url = parseResult.GetValue(_urlOption);
        var apiKey = parseResult.GetValue(_apiKeyOption);
        if (apiKey is null && !string.IsNullOrWhiteSpace(url))
        {
            apiKey = Environment.GetEnvironmentVariable("MAGPIE_API_KEY");
        }

        return new CommandTransportOptions(
            Url: url,
            ApiKey: apiKey,
            IsUrlExplicit: WasExplicit(parseResult, _urlOption),
            IsApiKeyExplicit: WasExplicit(parseResult, _apiKeyOption));
    }
}
