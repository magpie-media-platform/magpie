using System;

using Microsoft.Extensions.Logging;

namespace Magpie.Sdk.Extensions;

/// <summary>
/// The services available to an extension lifecycle hook.
/// </summary>
/// <param name="NodeId">The unique id of the current node</param>
/// <param name="ServiceProvider">The scoped service provider</param>
/// <param name="LoggerFactory">The factory for extension loggers</param>
public sealed record ExtensionLifecycleContext(
    string NodeId,
    IServiceProvider ServiceProvider,
    ILoggerFactory LoggerFactory);
