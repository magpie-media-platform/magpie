using System;

namespace Magpie.Extensions.IntegrationTestShims.Shared;

internal sealed class ScenarioFileMissingException(
    string pluginSlug,
    string scenarioName,
    string expectedPath,
    string methodName)
    : Exception(BuildMessage(pluginSlug, scenarioName, expectedPath, methodName))
{
    private static string BuildMessage(
        string pluginSlug,
        string scenarioName,
        string expectedPath,
        string methodName)
    {
        return $"The scenario fixture does not exist. Plugin: {pluginSlug}. Scenario: {scenarioName}. " +
            $"Method: {methodName}. File: {expectedPath}.";
    }
}
