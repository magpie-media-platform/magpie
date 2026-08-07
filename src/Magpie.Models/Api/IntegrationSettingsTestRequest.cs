using System;
using System.Collections.Generic;

namespace Magpie.Models.Api;

/// <summary>
/// The request for a settings test of an external service integration.
/// </summary>
public sealed record IntegrationSettingsTestRequest
{
    /// <summary>
    /// The provider type of the integration to test.
    /// </summary>
    public string? ProviderType { get; init; }

    /// <summary>
    /// The id of the stored integration to test.
    /// </summary>
    public Guid? Id { get; init; }

    /// <summary>
    /// The settings to test, keyed by setting key.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Settings { get; init; }
}
