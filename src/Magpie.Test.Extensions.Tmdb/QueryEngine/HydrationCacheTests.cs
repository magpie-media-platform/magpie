using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb;
using Magpie.Models.MetadataSearch;

using Microsoft.Extensions.Hosting;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tmdb.QueryEngine;

[TestFixture]
public class HydrationCacheTests
{
    [Test]
    public async Task GetOrCreateAsync_SharesOneLoad_WhenWaitersCancelIndependently()
    {
        // Arrange
        var cache = new HydrationCache(Mock.Of<IHostApplicationLifetime>());
        var key = HydrationCache.HydrationKey.Create("tmdb", "movie", 550, "en-US");
        var release = new TaskCompletionSource<MetadataSearchCandidate?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var cancelled = new CancellationTokenSource();

        var first = cache.GetOrCreateAsync(
            key,
            release,
            (state, _) =>
            {
                calls++;
                return state.Task;
            },
            cancelled.Token);
        var second = cache.GetOrCreateAsync(
            key,
            release,
            (state, _) =>
            {
                calls++;
                return state.Task;
            },
            default);

        // Act
        await cancelled.CancelAsync().ConfigureAwait(false);
        Assert.ThrowsAsync<OperationCanceledException>(async () => await first.ConfigureAwait(false));
        release.SetResult(null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await second.ConfigureAwait(false), Is.Null);
            Assert.That(calls, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task GetOrCreateAsync_CancelsTheLoad_WhenTheApplicationStops()
    {
        // Arrange
        using var application = new CancellationTokenSource();
        var cache = new HydrationCache(
            Mock.Of<IHostApplicationLifetime>(host => host.ApplicationStopping == application.Token));
        var key = HydrationCache.HydrationKey.Create("tmdb", "movie", 550, "en-US");
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loading = cache.GetOrCreateAsync(
            key,
            started,
            static async (state, token) =>
            {
                state.SetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                return null;
            },
            default);

        // Act
        await started.Task.ConfigureAwait(false);
        await application.CancelAsync().ConfigureAwait(false);

        // Assert
        Assert.CatchAsync<OperationCanceledException>(async () => await loading.ConfigureAwait(false));
    }
}
