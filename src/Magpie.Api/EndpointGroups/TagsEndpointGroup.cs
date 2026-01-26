using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Data;
using Magpie.Models;
using Magpie.Models.Result;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using HttpResult = Microsoft.AspNetCore.Http.IResult;

namespace Magpie.Api.EndpointGroups;

internal sealed class TagsEndpointGroup(ITagRepository tagRepository) : IApiEndpointGroup
{
    public string Name => "tags";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAllTags)
            .WithName("GetAllTags")
            .WithTags("Tags")
            .Produces<Tag[]>()
            .RequireAuthorization();

        builder.MapGet("/by-prefix/{prefix}", GetTagsByPrefix)
            .WithName("GetTagsByPrefix")
            .WithTags("Tags")
            .Produces<Tag[]>()
            .RequireAuthorization();
    }

    private async Task<HttpResult> GetAllTags(CancellationToken cancellationToken)
    {
        var result = await tagRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Tag>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Tag>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetTagsByPrefix(string prefix, CancellationToken cancellationToken)
    {
        var result = await tagRepository.GetByPrefixAsync(prefix, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Tag>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Tag>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
