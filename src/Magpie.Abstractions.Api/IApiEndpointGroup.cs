using Microsoft.AspNetCore.Routing;

namespace Magpie.Abstractions.Api;

/// <summary>
/// Defines a group of related API endpoints.
/// </summary>
/// <remarks>
/// Implementations register their endpoints with the provided route builder. Each group
/// is mapped under a common prefix based on its <see cref="Name"/> property.
/// </remarks>
public interface IApiEndpointGroup
{
    /// <summary>
    /// The name of the endpoint group, used as the route prefix.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Maps the endpoints for this group to the provided route builder.
    /// </summary>
    /// <param name="builder">The route builder to map endpoints to.</param>
    void MapEndpoints(IEndpointRouteBuilder builder);
}
