using System;
using System.Collections.Generic;
using System.Net.Http;

namespace Magpie.Sdk.Http;

/// <summary>
/// Sets transport options for provider HTTP requests.
/// </summary>
public static class UpstreamRequestExtensions
{
    private const string TimeoutKey = "Magpie.TransportTimeout";

    /// <summary>
    /// Sets a shorter HTTP request timeout, including upstream cooldown waits.
    /// </summary>
    /// <param name="request">The provider request</param>
    /// <param name="timeout">The request timeout</param>
    public static void SetTransportTimeout(this HttpRequestMessage request, TimeSpan timeout)
    {
        GetOptions(request)[TimeoutKey] = timeout;
    }

    internal static TimeSpan? GetTransportTimeout(this HttpRequestMessage request)
    {
        return GetOptions(request).TryGetValue(TimeoutKey, out var value) && value is TimeSpan timeout ? timeout : null;
    }

    internal static void CopyOptionsTo(this HttpRequestMessage request, HttpRequestMessage target)
    {
        var targetOptions = GetOptions(target);
        foreach (var option in GetOptions(request))
        {
            targetOptions[option.Key] = option.Value;
        }
    }

    private static IDictionary<string, object?> GetOptions(HttpRequestMessage request)
    {
#pragma warning disable CS0618 // Properties is the shared option store on both target frameworks.
        return request.Properties;
#pragma warning restore CS0618
    }
}
