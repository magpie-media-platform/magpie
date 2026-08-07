using System;

namespace Magpie.Api;

/// <summary>
/// The attribute that marks an endpoint handler as a generated Magpie CLI command.
/// </summary>
/// <param name="command">The name of the generated CLI command</param>
/// <param name="verb">The HTTP verb of the endpoint</param>
/// <param name="routeTemplate">The API route template, relative to the command group</param>
/// <param name="description">The description text of the generated CLI command</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MagpieCommandAttribute(
    string command,
    string verb = "GET",
    string routeTemplate = "",
    string? description = default) : Attribute
{
    /// <summary>
    /// The name of the generated CLI command.
    /// </summary>
    public string Command { get; } = command;

    /// <summary>
    /// The HTTP verb of the endpoint.
    /// </summary>
    public string Verb { get; } = verb;

    /// <summary>
    /// The API route template, relative to the command group.
    /// </summary>
    public string RouteTemplate { get; } = routeTemplate;

    /// <summary>
    /// The description text of the generated CLI command.
    /// </summary>
    public string? Description { get; } = description;
}
