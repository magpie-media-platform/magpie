using System;

namespace Magpie.Abstractions.Extensions;

/// <summary>
/// Context provided to extension lifecycle hooks.
/// </summary>
/// <param name="NodeId">The unique id of the current node (for multi-node awareness).</param>
/// <param name="Services">The service provider for resolving scoped services.</param>
public sealed record ExtensionLifecycleContext(string NodeId, IServiceProvider Services);
