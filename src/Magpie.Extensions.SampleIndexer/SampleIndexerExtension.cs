using System;

using Magpie.Abstractions;
using Magpie.Abstractions.Integrations;

using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Extensions.SampleIndexer;

/// <summary>
/// Reference extension that registers a single hardcoded <see cref="IIndexerIntegration"/>.
/// </summary>
/// <remarks>
/// Demonstrates the minimum surface area needed to ship a Magpie extension: a public
/// parameterless type implementing <see cref="IExtension"/> that registers its services
/// against the host DI container.
/// </remarks>
public sealed class SampleIndexerExtension : IExtension
{
    /// <summary>
    /// The stable extension id.
    /// </summary>
    public static readonly Guid ExtensionId = new("8B0F2A4C-1F3E-4A55-9E8E-2A6B9F1F0001");

    /// <inheritdoc />
    public Guid Id => ExtensionId;

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public string Name => "Sample Indexer";

    /// <inheritdoc />
    public string? TargetABI => "[1.0.0,)";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IIndexerIntegration, SampleIndexerIntegration>();
    }
}
