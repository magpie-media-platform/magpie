using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Sdk.Api;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Extensions.Jellyfin;

internal sealed class JellyfinLibrariesEndpointGroup : IApiEndpointGroup
{
    public string Name => Constants.ProviderTypeName;

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapPost("libraries", ListLibrariesAsync)
            .WithName(nameof(ListLibrariesAsync))
            .Produces<IReadOnlyList<SettingSchemaOption>>()
            .RequireManager();
    }

    internal async Task<IResult> ListLibrariesAsync(
        [FromServices] JellyfinLibraryService libraryService,
        [FromBody] JellyfinLibrariesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await libraryService
            .ListLibrariesAsync(request.MediaServerId, request.Settings, cancellationToken)
            .ConfigureAwait(false);

        return result.FailureKind switch
        {
            ConfigurationFailureKind.None => Results.Ok(result.Options),
            ConfigurationFailureKind.Fatal => Results.Problem(statusCode: StatusCodes.Status400BadRequest),
            ConfigurationFailureKind.Auth => Results.Problem(statusCode: StatusCodes.Status401Unauthorized),
            _ => Results.Problem(statusCode: StatusCodes.Status502BadGateway)
        };
    }
}
