using System;

using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Abstractions;

/// <summary>
/// Entry point for a Magpie extension.
/// </summary>
/// <remarks>
/// Extensions are loaded at startup and given the opportunity to register services,
/// CLI options, and other components with the host. An extension may provide one or
/// many capabilities (content types, metadata providers, integrations, etc.) or simply
/// add cross-cutting functionality.
/// </remarks>
public interface IExtension
{
    /// <summary>
    /// The unique identifier of the extension.
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// The friendly display name of the extension.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The target ABI of the extension.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The target ABI declares the target magpie version the extension is compatible with.
    /// </para>
    /// <para>
    /// Nuget style version ranges are supported like e.g. "[1.0.0, 2.0.0)"
    /// </para>
    /// </remarks>
    string? TargetABI { get; }

    /// <summary>
    /// Registers services provided by this extension.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    void RegisterServices(IServiceCollection services);
}
