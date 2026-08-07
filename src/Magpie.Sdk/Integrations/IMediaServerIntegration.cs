using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Api;

namespace Magpie.Sdk.Integrations;

/// <summary>
/// The integration contract for an external media server.
/// </summary>
public interface IMediaServerIntegration
{
    /// <summary>
    /// The provider type slug.
    /// </summary>
    string ProviderType { get; }

    /// <summary>
    /// The schema of the integration settings.
    /// </summary>
    IReadOnlyList<SettingSchemaDefinition> SettingsSchema { get; }

    /// <summary>
    /// The supported content type slugs, or an empty list for all content types.
    /// </summary>
    IReadOnlyList<string> SupportedContentTypes => [];

    /// <summary>
    /// Tests the connection to the media server.
    /// </summary>
    /// <param name="settings">The settings of the configured provider</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The result of the connection test.</returns>
    Task<ConfigurationTestResult> TestConnectionAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);
}
