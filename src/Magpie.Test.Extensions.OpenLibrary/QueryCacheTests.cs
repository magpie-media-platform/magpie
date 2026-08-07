using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary.QueryEngine;

using NUnit.Framework;

namespace Magpie.Test.Extensions.OpenLibrary;

[TestFixture]
public sealed class QueryCacheTests
{
    [Test]
    public async Task GetSourcePageAsync_SharedOperationCompletes_WhenOneWaiterCancels()
    {
        // Arrange
        var subject = new QueryCache();
        var completion = new TaskCompletionSource<SourcePage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var cancellation = new CancellationTokenSource();

        // Act
        var first = subject.GetSourcePageAsync(
            "same",
            exactClass: false,
            operationToken =>
            {
                calls++;
                Assert.That(operationToken, Is.EqualTo(CancellationToken.None));
                return completion.Task;
            },
            cancellation.Token);
        var second = subject.GetSourcePageAsync(
            "same",
            exactClass: false,
            _ => throw new AssertionException("The coalesced factory must not run twice."),
            default);
        await cancellation.CancelAsync().ConfigureAwait(false);
        Assert.That(async () => await first.ConfigureAwait(false), Throws.TypeOf<OperationCanceledException>());
        completion.SetResult(new SourcePage
        {
            NumFound = 0,
            NumFoundExact = true,
            Offset = 0,
            Hits = [],
            SourceRowCount = 0
        });
        var result = await second.ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.NumFound, Is.Zero);
            Assert.That(calls, Is.EqualTo(1));
        }
    }

    [Test]
    public void GetSourcePageAsync_RunsFactoryAgain_WhenPreviousOperationFailed()
    {
        // Arrange
        var subject = new QueryCache();
        var calls = 0;

        // Act
        Assert.That(
            async () => await subject.GetSourcePageAsync(
                "failure",
                exactClass: false,
                _ =>
                {
                    calls++;
                    return Task.FromException<SourcePage>(new HttpRequestException("failed"));
                },
                default).ConfigureAwait(false),
            Throws.TypeOf<HttpRequestException>());
        Assert.That(
            async () => await subject.GetSourcePageAsync(
                "failure",
                exactClass: false,
                _ =>
                {
                    calls++;
                    return Task.FromException<SourcePage>(new HttpRequestException("failed again"));
                },
                default).ConfigureAwait(false),
            Throws.TypeOf<HttpRequestException>());

        // Assert
        Assert.That(calls, Is.EqualTo(2));
    }
}
