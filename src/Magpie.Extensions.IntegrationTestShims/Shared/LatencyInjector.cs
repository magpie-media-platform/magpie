using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.IntegrationTestShims.Shared;

internal sealed class LatencyInjector(string pluginSlug)
{
    private const string RootEnvironmentVariable = "MAGPIE_TEST_FIXTURES_ROOT";
    private const string DefaultRoot = "/test-fixtures";

    private static readonly JsonSerializerOptions DefaultOptions = new(JsonSerializerDefaults.Web);

    private readonly string _pluginSlug = string.IsNullOrWhiteSpace(pluginSlug)
        ? throw new ArgumentException("Plugin slug must be non-empty.", nameof(pluginSlug))
        : pluginSlug;

    public async Task DelayAsync(string methodName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            throw new ArgumentException("Method name must be non-empty.", nameof(methodName));
        }

        var milliseconds = ReadConfiguredDelay(methodName);
        if (milliseconds <= 0)
        {
            return;
        }

        await Task.Delay(milliseconds, cancellationToken).ConfigureAwait(false);
    }

    private int ReadConfiguredDelay(string methodName)
    {
        string scenario;
        try
        {
            scenario = new ScenarioReader(_pluginSlug).ReadActiveScenario();
        }
        catch (FileNotFoundException)
        {
            return 0;
        }

        var latencyPath = Path.Combine(ScenarioFolder(scenario), "_latency.json");
        if (!File.Exists(latencyPath))
        {
            return 0;
        }

        Dictionary<string, int>? map;
        using (var stream = new FileStream(latencyPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            map = JsonSerializer.Deserialize<Dictionary<string, int>>(stream, DefaultOptions);
        }

        return map is not null && map.TryGetValue(methodName, out var ms) ? ms : 0;
    }

    private string ScenarioFolder(string scenario)
    {
        var overrideRoot = Environment.GetEnvironmentVariable(RootEnvironmentVariable);
        var root = string.IsNullOrWhiteSpace(overrideRoot) ? DefaultRoot : overrideRoot!;
        return Path.Combine(root, _pluginSlug, "scenarios", scenario);
    }
}
