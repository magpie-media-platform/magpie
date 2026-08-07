using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.IntegrationTestShims.Shared;

internal sealed class ScenarioReader(string pluginSlug)
{
    private const string RootEnvironmentVariable = "MAGPIE_TEST_FIXTURES_ROOT";
    private const string DefaultRoot = "/test-fixtures";

    private static readonly JsonSerializerOptions DefaultOptions =
        new(JsonSerializerDefaults.Web)
        {
            PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
            Converters = { new JsonStringEnumConverter() },
        };

    private readonly string _pluginSlug = string.IsNullOrWhiteSpace(pluginSlug)
        ? throw new ArgumentException("Plugin slug must be non-empty.", nameof(pluginSlug))
        : pluginSlug;

    public string ReadActiveScenario()
    {
        var pointer = Path.Combine(PluginRoot(), "_active");
        if (!File.Exists(pointer))
        {
            throw new FileNotFoundException(
                $"Active scenario pointer missing for plugin {_pluginSlug}. Expected file: {pointer}.",
                pointer);
        }

        var raw = File.ReadAllText(pointer).Trim();
        if (string.IsNullOrEmpty(raw))
        {
            throw new InvalidOperationException(
                $"Active scenario pointer for plugin {_pluginSlug} is empty. File: {pointer}.");
        }

        return raw;
    }

    public async Task<T> ReadAsync<T>(
        string methodName,
        string? discriminator = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            throw new ArgumentException("Method name must be non-empty.", nameof(methodName));
        }

        var scenario = ReadActiveScenario();
        var folder = Path.Combine(PluginRoot(), "scenarios", scenario);
        var fallback = Path.Combine(folder, methodName + ".json");
        var path = fallback;
        string? discriminated = null;

        if (!string.IsNullOrWhiteSpace(discriminator))
        {
            discriminated = Path.Combine(folder, $"{methodName}-{discriminator}.json");
            if (File.Exists(discriminated))
            {
                path = discriminated;
            }
        }

        if (!File.Exists(path))
        {
            throw new ScenarioFileMissingException(_pluginSlug, scenario, discriminated ?? path, methodName);
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        var value = await JsonSerializer
            .DeserializeAsync<T>(stream, DefaultOptions, cancellationToken)
            .ConfigureAwait(false);
        if (value is null)
        {
            throw new InvalidOperationException(
                $"The scenario fixture contains null. Plugin: {_pluginSlug}. Scenario: {scenario}. " +
                $"Method: {methodName}. File: {path}.");
        }

        return value;
    }

    private string PluginRoot()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(RootEnvironmentVariable);
        var root = string.IsNullOrWhiteSpace(overrideRoot) ? DefaultRoot : overrideRoot!;
        return Path.Combine(root, _pluginSlug);
    }
}
