using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;

namespace Magpie.Sdk.Integrations;

/// <summary>
/// An integration that validates its own settings.
/// </summary>
public interface IValidatesIntegrationSettings
{
    /// <summary>
    /// Validates the integration settings.
    /// </summary>
    /// <param name="settings">The settings of the configured provider</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The result of the validation.</returns>
    Task<IntegrationSettingsValidationResult> ValidateSettingsAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);
}
