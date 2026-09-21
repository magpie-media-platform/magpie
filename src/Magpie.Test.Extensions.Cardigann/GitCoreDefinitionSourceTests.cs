using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Cardigann.Git;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;
using Moq.Protected;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Cardigann;

[TestFixture]
public sealed class GitCoreDefinitionSourceTests
{
    [Test]
    public async Task LoadDefinitionsAsync_AbortsTheInFlightRequest_WhenCancelledDuringFetch()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource();
        var requestToken = CancellationToken.None;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage _, CancellationToken token) =>
            {
                requestToken = token;
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                return new HttpResponseMessage();
            });
        using var http = new HttpClient(handler.Object) { Timeout = Timeout.InfiniteTimeSpan };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(http);
        var source = new GitCoreDefinitionSource(factory.Object, NullLogger<GitCoreDefinitionSource>.Instance);

        // Act
        var load = source.LoadDefinitionsAsync(
            "https://git.test/owner/repo", "main", "definitions", null, cancellation.Token);
        await started.Task.ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(requestToken.IsCancellationRequested, Is.True);
            Assert.That(
                async () => await load.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }
    }
}
