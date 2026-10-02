using System;

namespace Magpie.Api;

/// <summary>
/// The attribute that marks an endpoint group as a generated Magpie CLI command group.
/// </summary>
/// <param name="name">The name of the generated CLI command group</param>
/// <param name="routePrefix">The API route prefix of the command group</param>
/// <param name="description">The description text of the generated CLI command group</param>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class MagpieCommandGroupAttribute(string name, string routePrefix, string description) : Attribute
{
    /// <summary>
    /// The name of the generated CLI command group.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// The API route prefix of the command group.
    /// </summary>
    public string RoutePrefix { get; } = routePrefix;

    /// <summary>
    /// The description text of the generated CLI command group.
    /// </summary>
    public string Description { get; } = description;
}
