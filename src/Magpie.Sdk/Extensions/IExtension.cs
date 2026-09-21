using System;

using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Sdk.Extensions;

/// <summary>
/// The entry point of a Magpie extension.
/// </summary>
public interface IExtension
{
    /// <summary>
    /// The id that correlates this extension to its installed record.
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// The extension version.
    /// </summary>
    Version Version { get; }

    /// <summary>
    /// The extension name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The ABI version that the extension targets.
    /// </summary>
    string? TargetABI { get; }

    /// <summary>
    /// Registers the services of the extension.
    /// </summary>
    /// <param name="services">The service collection of the host</param>
    void RegisterServices(IServiceCollection services);
}
