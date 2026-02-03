using System;

namespace Magpie.Models;

/// <summary>
/// Context provided to extension lifecycle hooks.
/// </summary>
/// <param name="NodeId">The unique id of the current node.</param>
/// <param name="Services">The service provider for resolving scoped services.</param>
public sealed record ExtensionLifecycleContext(string NodeId, IServiceProvider Services);
