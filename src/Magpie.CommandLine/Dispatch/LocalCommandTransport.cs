using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;

namespace Magpie.CommandLine.Dispatch;

internal sealed class LocalCommandTransport(HttpClient client) : ICommandTransport
{
    public static async Task<LocalCommandTransport> CreateAsync(
        Func<CancellationToken, Task<WebApplication>>? localAppFactory,
        CancellationToken cancellationToken)
    {
        if (localAppFactory is null)
        {
            throw new CommandTargetResolutionException(
                "This command cannot start Magpie locally. Provide --url to target a remote host.");
        }

        var app = await localAppFactory(cancellationToken).ConfigureAwait(false);

        return new LocalCommandTransport(app.GetTestClient());
    }

    public Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return client.SendAsync(request, completionOption, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        client.Dispose();
        return ValueTask.CompletedTask;
    }
}
