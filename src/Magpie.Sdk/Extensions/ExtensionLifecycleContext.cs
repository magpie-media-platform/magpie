using System;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Logging;

namespace Magpie.Sdk.Extensions;

/// <summary>
/// The services available to an extension lifecycle hook.
/// </summary>
/// <param name="NodeId">The unique id of the current node</param>
/// <param name="ServiceProvider">The scoped service provider</param>
/// <param name="LoggerFactory">The factory for extension loggers</param>
[method: SetsRequiredMembers]
public sealed record ExtensionLifecycleContext(
    string NodeId,
    IServiceProvider ServiceProvider,
    ILoggerFactory LoggerFactory)
{
    /// <summary>
    /// The unique id of the current node.
    /// </summary>
    public required string NodeId { get; set; } = NodeId;

    /// <summary>
    /// The scoped service provider.
    /// </summary>
    public IServiceProvider ServiceProvider { get; set; } = ServiceProvider;

    /// <summary>
    /// The factory for extension loggers.
    /// </summary>
    public ILoggerFactory LoggerFactory { get; set; } = LoggerFactory;
}
