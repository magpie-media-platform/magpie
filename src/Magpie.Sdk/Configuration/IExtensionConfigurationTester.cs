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
    /// The settings group name.
    /// </summary>
    string SettingsGroupName { get; }

    /// <summary>
    /// Tests the configuration using the supplied settings values.
    /// </summary>
    /// <param name="settings">The settings values to test</param>
    /// <param name="cancellationToken">The cancellation token</param>
    Task<ConfigurationTestResult> TestConfigurationAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken);
}
