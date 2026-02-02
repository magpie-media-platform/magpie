using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Auth;
using Magpie.Api.Request;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using static Magpie.Abstractions.Auth.ITokenService;

namespace Magpie.Api.EndpointGroups;

internal sealed class AuthEndpointGroup(
    ICredentialAuthenticationProvider authProvider,
    ITokenService tokenService) : IApiEndpointGroup
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

    private async Task<IResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.Problem("Username and password are required", statusCode: StatusCodes.Status400BadRequest);
        }

        var user = await authProvider.VerifyCredentialsAsync(request.Username, request.Password, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return Results.Problem("Invalid username or password", statusCode: StatusCodes.Status401Unauthorized);
        }

        var token = await tokenService.GenerateAccessTokenAsync(user, cancellationToken).ConfigureAwait(false);

        return Results.Ok(token);
    }
}
