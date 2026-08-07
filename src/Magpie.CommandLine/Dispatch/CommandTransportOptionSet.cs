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
        Description = "API key for remote commands."
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
        return new CommandTransportOptions(
            Url: parseResult.GetValue(_urlOption),
            ApiKey: parseResult.GetValue(_apiKeyOption),
            IsUrlExplicit: WasExplicit(parseResult, _urlOption),
            IsApiKeyExplicit: WasExplicit(parseResult, _apiKeyOption));
    }
}
