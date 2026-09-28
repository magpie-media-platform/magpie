using System.Collections.Generic;

namespace Magpie.Models.Integrations;

/// <summary>
/// A provider of one integration kind, with its settings schema.
/// </summary>
public sealed record ProviderDescriptor
{
    /// <summary>
    /// The integration kind that the provider belongs to.
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>
    /// The type name of the provider.
    /// </summary>
    public required string ProviderType { get; set; }

    /// <summary>
    /// The display name of the provider.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// The settings schema of the provider.
    /// </summary>
    public required IReadOnlyList<SettingSchemaDefinition> Schema { get; set; }
}
