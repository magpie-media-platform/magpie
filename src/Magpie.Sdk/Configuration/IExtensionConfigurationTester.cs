using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;

namespace Magpie.Sdk.Configuration;

/// <summary>
/// Tests an extension-owned settings group's configuration against the live service it describes.
/// </summary>
public interface IExtensionConfigurationTester
{
    /// <summary>
    /// The extension that owns the settings group.
    /// </summary>
    Guid ExtensionId { get; }

    /// <summary>
    /// The name of the settings group that this tester validates.
    /// </summary>
    string SettingsGroupName { get; }

    /// <summary>
    /// Tests the configuration using the supplied settings values.
    /// </summary>
    /// <param name="settings">The settings values to test</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ConfigurationTestResult> TestConfigurationAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken);
}
