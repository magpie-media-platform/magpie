using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Data;
using Magpie.Models.Result;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using HttpResult = Microsoft.AspNetCore.Http.IResult;

namespace Magpie.Api.EndpointGroups;

/// <summary>
/// Endpoint group for download client management endpoints.
/// </summary>
internal sealed class DownloadClientsEndpointGroup(
    IDownloadClientRepository downloadClientRepository) : IApiEndpointGroup
{
    /// <inheritdoc />
    public string Name => "download-clients";

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapDelete("/{id:guid}", DeleteDownloadClient)
            .WithName("DeleteDownloadClient")
            .WithTags("DownloadClients")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }


    private async Task<HttpResult> DeleteDownloadClient(Guid id, CancellationToken cancellationToken)
    {
        var result = await downloadClientRepository.DeleteAsync(id, deletedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> { Value: false } => Results.NotFound(),
            SuccessResult<bool> => Results.NoContent(),
            FailureResult<bool> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
