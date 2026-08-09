using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.IntegrationTestShims.Shared;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Enums;
using Magpie.Sdk.Configuration;

namespace Magpie.Extensions.IntegrationTestShims.Settings;

internal sealed class MockSettingsGroup : IExtensionSettingsSchemaProvider, IExtensionConfigurationTester
{
    internal const string GroupName = "integration-test-shims";
    internal const string ProviderTypeName = "mock-settings";

    private readonly ScenarioReader _scenarios = new(ProviderTypeName);
    private readonly AuditWriter _audit = new(ProviderTypeName);

    public Guid ExtensionId => IntegrationTestShimsExtension.ExtensionId;

    public string SettingsGroupName => GroupName;

    public IEnumerable<ExtensionSettingsGroupDefinition> GetSettingsGroups()
    {
        return
        [
            new ExtensionSettingsGroupDefinition
            {
                ExtensionId = ExtensionId,
                Name = GroupName,
                SettingsSchema =
                [
                    new SettingSchemaDefinition
                    {
                        Key = "endpoint",
                        Type = DataType.String,
                        DisplayName = "Endpoint",
                        Required = true
                    }
                ]
            },
            new ExtensionSettingsGroupDefinition
            {
                ExtensionId = ExtensionId,
                Name = "Primary hierarchy shim",
                SettingsSchema =
                [
                    new SettingSchemaDefinition
                    {
                        Key = "providerType",
                        Type = DataType.String
                    }
                ]
            }
        ];
    }

    public async Task<ConfigurationTestResult> TestConfigurationAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        const string method = nameof(TestConfigurationAsync);
        var response = await _scenarios
            .ReadAsync<ConfigurationTestResult>(method, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var settingsKeys = settings.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        await _audit.WriteAsync(method, new { settingsKeys }, response, cancellationToken).ConfigureAwait(false);
        return response;
    }
}
