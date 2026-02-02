using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Core.Security;
using Magpie.Models.Data;

using Microsoft.IdentityModel.Tokens;

using static Magpie.Abstractions.Auth.ITokenService;

namespace Magpie.Auth;

internal sealed class TokenService(
    TokenServiceOptions options,
    IRefreshTokenRepository refreshTokenRepository,
    IKeyStore keyStore) : ITokenService
{
    private const string JwtSigningKeyName = "jwt-signing";

    private readonly JwtSecurityTokenHandler _tokenHandler = new();
    private readonly Lazy<Task<SymmetricSecurityKey>> _signingKeyTask = new(() => InitializeKeyAsync(keyStore));

    public async Task<AccessToken> GenerateAccessTokenAsync(
        User user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var signingKey = await _signingKeyTask.Value.ConfigureAwait(false);
        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.Add(options.AccessTokenLifetime);
        var principal = user.ToPrincipal(
        [
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(issuedAt).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        ]);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = principal.Identity as ClaimsIdentity,
            Expires = expiresAt,
            Issuer = options.Issuer,
            Audience = options.Audience,
            SigningCredentials = new(signingKey, SecurityAlgorithms.HmacSha256Signature)
        };

        var token = _tokenHandler.CreateToken(tokenDescriptor);
        return new AccessToken(_tokenHandler.WriteToken(token), expiresAt);
    }

    public async Task<string> GenerateRefreshTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(64);
        var token = Convert.ToBase64String(tokenBytes);
        var tokenHash = ComputeHash(token);
        var now = DateTime.UtcNow;

        await refreshTokenRepository.CreateAsync(new()
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = now + options.RefreshTokenLifetime,
            CreatedAt = now
        }, cancellationToken).ConfigureAwait(false);

        return token;
    }

    /// <inheritdoc />
    public async Task<Guid?> TryValidateRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var tokenHash = ComputeHash(refreshToken);
        var storedToken = await refreshTokenRepository.GetByHashAsync(tokenHash, cancellationToken)
            .ConfigureAwait(false);

        if (storedToken is null)
        {
            return null;
        }

        if (storedToken.ExpiresAt <= DateTime.UtcNow)
        {
            return null;
        }

        return storedToken.UserId;
    }

    public Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        return refreshTokenRepository.DeleteAsync(ComputeHash(refreshToken), cancellationToken);
    }

    public async Task<ClaimsPrincipal?> TryValidateAccessTokenAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var signingKey = await _signingKeyTask.Value.ConfigureAwait(false);
        try
        {
            return _tokenHandler.ValidateToken(accessToken, new()
            {
                ValidateIssuer = true,
                ValidIssuer = options.Issuer,
                ValidateAudience = true,
                ValidAudience = options.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signingKey,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out _);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }

    private static async Task<SymmetricSecurityKey> InitializeKeyAsync(IKeyStore keyStore)
    {
        var exists = await keyStore.KeyExistsAsync(JwtSigningKeyName).ConfigureAwait(false);
        if (!exists)
        {
            await keyStore.CreateSigningKeyAsync(JwtSigningKeyName).ConfigureAwait(false);
        }
        var keyBytes = await keyStore.GetPublicKeyAsync(JwtSigningKeyName).ConfigureAwait(false);

        return new SymmetricSecurityKey(keyBytes);
    }

    private static string ComputeHash(string token)
    {
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
