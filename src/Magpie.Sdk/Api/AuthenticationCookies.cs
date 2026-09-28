using System.Globalization;

using Microsoft.AspNetCore.Http;

namespace Magpie.Sdk.Api;

/// <summary>
/// Provides cookie names that isolate authentication on different server ports.
/// </summary>
public static class AuthenticationCookies
{
    /// <summary>
    /// Gets the authentication cookie name for the request port.
    /// </summary>
    /// <param name="name">The base cookie name.</param>
    /// <param name="request">The current HTTP request.</param>
    public static string GetName(string name, HttpRequest request)
    {
        var port = request.Host.Port ?? (request.IsHttps ? 443 : 80);
        return name + "." + port.ToString(CultureInfo.InvariantCulture);
    }
}
