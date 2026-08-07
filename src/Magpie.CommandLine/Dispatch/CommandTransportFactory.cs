using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;

namespace Magpie.CommandLine.Dispatch;

internal sealed class CommandTransportFactory(Func<CancellationToken, Task<WebApplication>>? localAppFactory = null)
{
    public async Task<ICommandTransport> CreateAsync(
        CommandTransportOptions options,
        bool allowsAnonymousRemote,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var target = Resolve(options, allowsAnonymousRemote);
        return target.Kind switch
        {
            CommandTransportKind.Local => await LocalCommandTransport
                .CreateAsync(localAppFactory, cancellationToken)
                .ConfigureAwait(false),
            CommandTransportKind.Remote => new RemoteCommandTransport(target.Url!, target.ApiKey),
            _ => throw new InvalidOperationException($"Unsupported command transport kind {target.Kind}.")
        };
    }

    private static CommandTarget Resolve(CommandTransportOptions options, bool allowAnonymousRemote = false)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.IsUrlExplicit && string.IsNullOrWhiteSpace(options.Url))
        {
            throw new CommandTargetResolutionException("Remote URL from --url must not be blank.");
        }

        var apiKeyWasExplicit = options.IsApiKeyExplicit || !string.IsNullOrWhiteSpace(options.ApiKey);
        if (string.IsNullOrWhiteSpace(options.Url))
        {
            if (apiKeyWasExplicit)
            {
                throw new CommandTargetResolutionException(
                    "Remote commands require --url when --api-key is supplied.");
            }

            return new CommandTarget(CommandTransportKind.Local, null, null);
        }

        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var url) ||
            url.Scheme is not ("http" or "https"))
        {
            throw new CommandTargetResolutionException(
                $"Remote URL {options.Url} is invalid. Use an absolute http or https URL.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            if (allowAnonymousRemote)
            {
                return new CommandTarget(CommandTransportKind.Remote, url, null);
            }

            throw new CommandTargetResolutionException(
                "Remote commands using --url require --api-key.");
        }

        return new CommandTarget(CommandTransportKind.Remote, url, options.ApiKey);
    }
}
