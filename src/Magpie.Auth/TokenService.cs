using System;
using System.Collections.Generic;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Repositories;
using Magpie.Models.Data;
using Magpie.Models.Security;
using Magpie.Sdk.Security;

using Microsoft.IdentityModel.Tokens;

using RefreshToken = Magpie.Models.Data.RefreshToken;

namespace Magpie.Auth;

internal sealed class TokenService(
    TokenServiceOptions options,
    IRefreshTokenRepository refreshTokenRepository,
    IKeyStore keyStore,
    CryptoProviderFactory cryptoProviderFactory,
    TimeProvider timeProvider) : ITokenService
{
    private const string JwtHmacKeyName = "jwt-hmac";

    private readonly JwtSecurityTokenHandler _tokenHandler = new();
    private readonly Lazy<Task<KeyStoreHmacSecurityKey>> _signingKeyTask = new(() => InitializeKeyAsync(keyStore));

    private static async Task<KeyStoreHmacSecurityKey> InitializeKeyAsync(IKeyStore keyStore)
    {
        var exists = await keyStore.KeyExistsAsync(JwtHmacKeyName).ConfigureAwait(false);
        if (!exists)
        {
            try
            {
                await keyStore.CreateHmacKeyAsync(JwtHmacKeyName).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                if (!await keyStore.KeyExistsAsync(JwtHmacKeyName).ConfigureAwait(false))
                {
                    throw;
                }
            }
        }

        return new KeyStoreHmacSecurityKey(keyStore, JwtHmacKeyName);
    }

    private static string ComputeHash(string token)
    {
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    public async Task<IssuedAccessToken> GenerateAccessTokenAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var signingKey = await _signingKeyTask.Value.ConfigureAwait(false);
        var issuedAt = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = issuedAt.Add(options.AccessTokenLifetime);
        var issuedAtSeconds = new DateTimeOffset(issuedAt).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var accessTokenJti = Guid.NewGuid().ToString();
        var principal = user.ToPrincipal(
        [
            new(JwtRegisteredClaimNames.Jti, accessTokenJti),
            new(JwtRegisteredClaimNames.Iat, issuedAtSeconds, ClaimValueTypes.Integer64)
        ]);

        var signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256Signature)
        {
            CryptoProviderFactory = cryptoProviderFactory
        };
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = principal.Identity as ClaimsIdentity,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = expiresAt,
            Issuer = options.Issuer,
            Audience = options.Audience,
            SigningCredentials = signingCredentials
        };

        var token = _tokenHandler.CreateToken(tokenDescriptor);
        var written = _tokenHandler.WriteToken(token);
        Telemetry.TokensIssued.Add(1, new KeyValuePair<string, object?>("kind", "access"));
        return new IssuedAccessToken(written, expiresAt, accessTokenJti);
    }

    public async Task<IssuedToken> GenerateRefreshTokenAsync(
        Guid userId,
        string accessTokenJti,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessTokenJti);

        var tokenBytes = RandomNumberGenerator.GetBytes(64);
        var token = Convert.ToBase64String(tokenBytes);
        var tokenHash = ComputeHash(token);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = now + options.RefreshTokenLifetime;

        await refreshTokenRepository.CreateAsync(
            new RefreshToken
            {
                UserId = userId,
                TokenHash = tokenHash,
                AccessTokenJti = accessTokenJti,
                ExpiresAt = expiresAt,
                CreatedAt = now
            },
            cancellationToken).ConfigureAwait(false);

        Telemetry.TokensIssued.Add(1, new KeyValuePair<string, object?>("kind", "refresh"));
        return new IssuedToken(token, expiresAt);
    }

    public async Task<Guid?> TryConsumeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var tokenHash = ComputeHash(refreshToken);
        var storedToken = await refreshTokenRepository
            .ConsumeAsync(tokenHash, timeProvider.GetUtcNow().UtcDateTime, cancellationToken)
            .ConfigureAwait(false);

        return storedToken?.UserId;
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
            var validationResult = await _tokenHandler.ValidateTokenAsync(
                accessToken,
                new()
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    CryptoProviderFactory = cryptoProviderFactory,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                }).ConfigureAwait(false);
            if (!validationResult.IsValid ||
                validationResult.ClaimsIdentity.FindFirst(JwtRegisteredClaimNames.Jti)?.Value is not { } jti)
            {
                return null;
            }

            var session = await refreshTokenRepository
                .GetByAccessTokenJtiAsync(jti, cancellationToken)
                .ConfigureAwait(false);
            return session?.ExpiresAt > timeProvider.GetUtcNow().UtcDateTime &&
                string.Equals(session.AccessTokenJti, jti, StringComparison.Ordinal)
                ? new ClaimsPrincipal(validationResult.ClaimsIdentity)
                : null;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }
}
