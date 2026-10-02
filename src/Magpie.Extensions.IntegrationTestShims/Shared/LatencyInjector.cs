using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.IntegrationTestShims.Shared;

internal sealed class LatencyInjector(string pluginSlug)
{
    private readonly ScenarioReader _scenarios = new(pluginSlug);

    public async Task DelayAsync(string methodName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            throw new ArgumentException("Method name must be non-empty.", nameof(methodName));
        }

        Dictionary<string, int> latencies;
        try
        {
            latencies = await _scenarios
                .ReadAsync<Dictionary<string, int>>("_latency", cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is FileNotFoundException or ScenarioFileMissingException)
        {
            return;
        }

        if (latencies.TryGetValue(methodName, out var milliseconds) && milliseconds > 0)
        {
            await Task.Delay(milliseconds, cancellationToken).ConfigureAwait(false);
        }
    }
}
