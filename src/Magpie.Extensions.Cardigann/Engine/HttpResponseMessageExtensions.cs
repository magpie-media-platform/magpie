using System;
using System.Net.Http;

namespace Magpie.Extensions.Cardigann.Engine;

internal static class HttpResponseMessageExtensions
{
    public static string? DetectCloudflareChallenge(this HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        var hasCfHeader = response.Headers.Contains("CF-RAY") || response.Headers.Contains("cf-mitigated");
        if ((status == 403 || status == 503) && hasCfHeader)
        {
            return $"HTTP {status} with CF headers";
        }

        if (status == 503 &&
            response.Headers.Server.ToString().IndexOf("cloudflare", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "HTTP 503 from cloudflare server";
        }

        if (status >= 520 && status <= 524 && hasCfHeader)
        {
            return $"HTTP {status} (Cloudflare origin error)";
        }

        return null;
    }
}
