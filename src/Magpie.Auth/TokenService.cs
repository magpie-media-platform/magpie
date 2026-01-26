using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Data;
using Magpie.Abstractions.Security;
using Magpie.Core;
using Magpie.Models.Result;

using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

using static Magpie.Abstractions.Auth.ITokenService;

using UserModel = Magpie.Models.Users.User;

namespace Magpie.Auth;

/// <summary>
/// Service for JWT token generation and validation.
/// </summary>
public sealed class TokenService(
    TokenServiceOptions options,
    IUserManager userManager,
    IRefreshTokenRepository refreshTokenRepository,
    IKeyStore keyStore,
    ILogger<TokenService> logger) : ITokenService
{
    private const string JwtSigningKeyName = "jwt-signing";

    private readonly JwtSecurityTokenHandler _tokenHandler = new();
    private readonly Lazy<Task<Result<SymmetricSecurityKey>>> _signingKeyTask = new(() => InitializeKeyAsync(keyStore));

    /// <inheritdoc />
    public async Task<Result<AccessToken>> GenerateAccessTokenAsync(
        UserModel user,
        CancellationToken cancellationToken = default)
    {
        if (user is null)
        {
            return Result.Fail<AccessToken>("user not found", ErrorCodes.TokenGenerationError);
        }
        return await userManager.CreatePrincipalAsync(user, cancellationToken)
            .ZipAsync(_signingKeyTask.Value)
            .MapAsync((principal, signingKey) =>
            {
                var issuedAt = DateTimeOffset.UtcNow;
                var expiresAt = issuedAt.Add(options.AccessTokenLifetime);
                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new Claim(ClaimTypes.Name, user.Username),
                        new Claim(ClaimTypes.Role, user.Role.ToString()),
                        new Claim(MagpieClaims.AuthProviderId, user.AuthProviderId?.ToString() ?? string.Empty),
                        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                        new Claim(
                            JwtRegisteredClaimNames.Iat,
                            issuedAt.ToUnixTimeSeconds().ToString(),
                            ClaimValueTypes.Integer64)
                    ]),
                    Expires = expiresAt.UtcDateTime,
                    Issuer = options.Issuer,
                    Audience = options.Audience,
                    SigningCredentials = new(signingKey, SecurityAlgorithms.HmacSha256Signature)
                };
                var token = _tokenHandler.CreateToken(tokenDescriptor);

                return new AccessToken(_tokenHandler.WriteToken(token), expiresAt);
            });
    }

    /// <inheritdoc />
    public Task<Result<string>> GenerateRefreshTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var rng = RandomNumberGenerator.Create();

        var tokenBytes = new byte[64];
        rng.GetBytes(tokenBytes);

        var token = Convert.ToBase64String(tokenBytes);
        var tokenHash = ComputeHash(token);
        var expiresAt = DateTimeOffset.UtcNow.Add(options.RefreshTokenLifetime);

        return refreshTokenRepository.CreateAsync(userId, tokenHash, expiresAt, cancellationToken)
            .MapAsync(_ => token);
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> ValidateRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var tokenHash = ComputeHash(refreshToken);

        return await refreshTokenRepository.GetByHashAsync(tokenHash, cancellationToken)
            .BindAsync(refreshToken => refreshToken switch
            {
                null =>
                    Result.Fail<Guid>("refresh token not found", ErrorCodes.RefreshTokenNotFound),
                { } r when r.ExpiresAt <= DateTimeOffset.UtcNow =>
                    Result.Fail<Guid>("refresh token has expired", ErrorCodes.RefreshTokenExpired),
                { UserId: var userId } =>
                    Result.Success(userId),
            })
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<Unit>> RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var tokenHash = ComputeHash(refreshToken);

        return await refreshTokenRepository.DeleteAsync(tokenHash, cancellationToken)
            .MapAsync(_ => Unit.Default)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<ClaimsPrincipal>> ValidateAccessTokenAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        return await _signingKeyTask.Value
            .BindAsync(signingKey =>
            {
                try
                {
                    var validationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = options.Issuer,
                        ValidateAudience = true,
                        ValidAudience = options.Audience,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = signingKey,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.Zero
                    };

                    var principal = _tokenHandler.ValidateToken(accessToken, validationParameters, out _);

                    return Result.Success(principal);
                }
                catch (SecurityTokenExpiredException)
                {
                    return Result.Fail<ClaimsPrincipal>("Access token has expired", ErrorCodes.TokenValidationError);
                }
                catch (SecurityTokenException ex)
                {
                    logger.LogWarning(ex, "Token validation failed");
                    return Result.Fail<ClaimsPrincipal>("Invalid access token", ErrorCodes.TokenValidationError);
                }
                catch (ArgumentException ex)
                {
                    logger.LogWarning(ex, "Token validation failed - malformed token");
                    return Result.Fail<ClaimsPrincipal>("Invalid access token", ErrorCodes.TokenValidationError);
                }
            })
            .ConfigureAwait(false);
    }

    private static async Task<Result<SymmetricSecurityKey>> InitializeKeyAsync(IKeyStore keyStore)
    {
        return await keyStore.KeyExistsAsync(JwtSigningKeyName)
            .BindAsync(async exists => exists
                ? Result.Success(Unit.Default)
                : await keyStore.CreateSigningKeyAsync(JwtSigningKeyName).ConfigureAwait(false))
            .BindAsync(_ => keyStore.GetPublicKeyAsync(JwtSigningKeyName))
            .MapAsync(keyBytes => new SymmetricSecurityKey(keyBytes))
            .ConfigureAwait(false);
    }

    private static string ComputeHash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }
}
