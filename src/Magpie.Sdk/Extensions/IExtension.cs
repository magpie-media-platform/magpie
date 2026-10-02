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
    Version Version
    {
        get
        {
            var version = GetType().Assembly.GetName().Version!;
            return new(version.Major, version.Minor, version.Build);
        }
    }

    /// <summary>
    /// The extension name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The target ABI of the extension, as a version or an interval such as <c>[1.4.0,1.6.0)</c>. The host loads the
    /// extension only when the host ABI satisfies the target ABI, has the same major version as the Magpie.Sdk that
    /// the extension is built against, and is not older than that version.
    /// </summary>
    string? TargetABI => null;

    /// <summary>
    /// Registers the services of the extension.
    /// </summary>
    /// <param name="services">The service collection of the host</param>
    void RegisterServices(IServiceCollection services);
}
