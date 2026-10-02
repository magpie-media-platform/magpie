using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.MusicBrainz.QueryEngine;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.MusicBrainz;

internal sealed class CoverArtArchiveClient(
    IHttpClientFactory httpClientFactory,
    ILogger<CoverArtArchiveClient> logger) : ICoverArtArchiveClient
{
    public const string HttpClientName = "magpie.coverartarchive";

    public const string DefaultBaseUrl = "https://coverartarchive.org/";

    private const string DefaultContact = "https://github.com/magpie-app/magpie";

    private static readonly HttpStatusCode PermanentRedirect = (HttpStatusCode)308;

    public async Task<FetchResult<Uri>> GetReleaseGroupFrontAsync(
        string releaseGroupMbid,
        int thumbnailSize,
        Settings settings,
        CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress ??= new Uri(DefaultBaseUrl, UriKind.Absolute);
        var target = releaseGroupMbid.ReleaseGroupFrontTarget(thumbnailSize);
        using var request = new HttpRequestMessage(HttpMethod.Head, new Uri(target, UriKind.Relative));
        request.Headers.TryAddWithoutValidation("User-Agent", BuildUserAgent(settings));
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new FetchResult<Uri> { Outcome = FetchOutcome.Timeout };
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Cover Art Archive transport failed ({Key})", DiagnosticIdentity(releaseGroupMbid));
            return new FetchResult<Uri> { Outcome = FetchOutcome.Unavailable };
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new FetchResult<Uri> { Outcome = FetchOutcome.NotFound };
            }

            if (response.StatusCode is HttpStatusCode.TemporaryRedirect || response.StatusCode == PermanentRedirect)
            {
                var location = response.Headers.Location;
                return location is { IsAbsoluteUri: true } &&
                    string.Equals(location.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                        ? new FetchResult<Uri> { Outcome = FetchOutcome.Success, Value = location }
                        : new FetchResult<Uri> { Outcome = FetchOutcome.Malformed };
            }

            logger.LogWarning(
                "Cover Art Archive returned {StatusCode} ({Key})",
                (int)response.StatusCode,
                DiagnosticIdentity(releaseGroupMbid));
            return new FetchResult<Uri> { Outcome = FetchOutcome.Unavailable };
        }
    }

    private static string BuildUserAgent(Settings settings)
    {
        var contact = string.IsNullOrWhiteSpace(settings.Contact) ? DefaultContact : settings.Contact.Trim();
        var version = typeof(CoverArtArchiveClient).Assembly.GetName().Version ?? new Version(1, 0, 0);
        return string.Format(
            CultureInfo.InvariantCulture,
            "Magpie/{0} ( {1} )",
            version.ToString(Math.Min(3, version.Build >= 0 ? 3 : 2)),
            contact);
    }

    private static string DiagnosticIdentity(string value)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToString(hash, 0, 6).Replace("-", string.Empty).ToLowerInvariant();
    }
}
