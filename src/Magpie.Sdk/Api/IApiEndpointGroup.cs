using Microsoft.AspNetCore.Routing;

namespace Magpie.Sdk.Api;

/// <summary>
/// The contract for a group of related API endpoints.
/// </summary>
public interface IApiEndpointGroup
{
    /// <summary>
    /// The route prefix of the group.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Maps the endpoints of the group onto a route builder.
    /// </summary>
    /// <param name="builder">The route builder that receives the endpoints</param>
    void MapEndpoints(IEndpointRouteBuilder builder);
}
