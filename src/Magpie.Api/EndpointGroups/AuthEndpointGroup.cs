using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Security;
using Magpie.Api.Request;
using Magpie.Models.Result;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using static Magpie.Abstractions.Auth.ITokenService;

using HttpResult = Microsoft.AspNetCore.Http.IResult;

namespace Magpie.Api.EndpointGroups;

internal sealed class AuthEndpointGroup(IUserManager userManager, ITokenService tokenService) : IApiEndpointGroup
{
    public string Name => "auth";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapPost("/login", Login)
            .WithName("AuthLogin")
            .WithTags("Auth")
            .Produces<AccessToken>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .AllowAnonymous();
    }

    private async Task<HttpResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.Problem("Username and password are required", statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await userManager.VerifyCredentialsAsync(request.Username, request.Password, cancellationToken)
            .BindAsync(user => user switch
            {
                null => Result.SuccessAsync<AccessToken?>(null),
                { } => tokenService.GenerateAccessTokenAsync(user, cancellationToken) as Task<Result<AccessToken?>>
            })
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<AccessToken?> { Value: { } token } =>
                Results.Ok(token),
            SuccessResult<AccessToken?> { Value: null } =>
                Results.Problem("Invalid username or password", statusCode: StatusCodes.Status401Unauthorized),
            FailureResult<AccessToken?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ =>
                Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
