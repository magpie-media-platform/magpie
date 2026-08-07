using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.CommandLine.Dispatch;

internal interface ICommandTransport : IAsyncDisposable
{
    Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken);
}
