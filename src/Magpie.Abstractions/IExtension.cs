using System;

using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Abstractions;

/// <summary>
/// Entry point for a Magpie extension.
/// </summary>
/// <remarks>
/// <para>
/// Extensions are loaded at startup and given the opportunity to register services,
/// CLI options, and other components with the host. An extension may provide one or
/// many capabilities (content types, metadata providers, integrations, etc.) or simply
/// add cross-cutting functionality.
/// </para>
/// <para>
/// Implementations must expose a public parameterless constructor: the host instantiates
/// extensions via <c>System.Activator.CreateInstance</c>. Authors that need dependency
/// injection should accept dependencies through services registered in
/// <see cref="RegisterServices"/>, or resolve them from the host service provider
/// during a lifecycle hook (e.g. <c>OnAppInitializeAsync</c>).
/// </para>
/// </remarks>
public interface IExtension
{
    /// <summary>
    /// The unique id of the extension.
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// The version of the extension. Used for ordering and update detection.
    /// </summary>
    Version Version { get; }

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
