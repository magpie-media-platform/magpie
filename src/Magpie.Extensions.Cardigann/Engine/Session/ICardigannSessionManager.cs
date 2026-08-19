using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.Cardigann.Engine.Session;

internal interface ICardigannSessionManager
{
    Task<CardigannLoginOutcome> EnsureLoggedInAsync(
        string sessionKey,
        LoginBlock login,
        Uri baseUri,
        IReadOnlyDictionary<string, object?> variables,
        string? cookieValue,
        string userAgent,
        bool forceRefresh,
        CancellationToken cancellationToken);

    Task<HttpResponseMessage> SendAsync(
        string sessionKey,
        Uri baseUri,
        HttpMethod method,
        Uri requestUri,
        string? formBody,
        string userAgent,
        bool captureCookies,
        CancellationToken cancellationToken);

    bool IsLoggedOut(LoginBlock login, HttpStatusCode statusCode, string body);

    void InvalidateSession(string sessionKey);
}
