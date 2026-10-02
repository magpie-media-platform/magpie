using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using AngleSharp.Html.Parser;

using Magpie.Extensions.Cardigann.Engine.Templating;
using Magpie.Models;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Cardigann.Engine.Session;

internal sealed class CardigannSessionManager(
    IHttpClientFactory clientFactory,
    IResultParser resultParser,
    ICloudflareSolver solver,
    CardigannSessionState state,
    TimeProvider timeProvider,
    ILogger<CardigannSessionManager> logger) : ICardigannSessionManager
{
    public const string HttpClientName = "magpie.cardigann";

    internal static readonly HttpRequestOptionsKey<string> AuthenticationScopeKey = new("magpie.upstream.auth");

    private const string MethodForm = "form";
    private const string MethodPost = "post";
    private const string MethodGet = "get";
    private const string MethodCookie = "cookie";
    private const string MethodOneUrl = "oneurl";
    private const string AcceptHeader = "text/html, application/xhtml+xml, */*; q=0.8";
    private const int MaxRedirects = 5;

    private static readonly TimeSpan TlsBindingLifetime = TimeSpan.FromMinutes(10);

    private readonly HtmlParser _htmlParser = new();

    private static bool IsRedirect(HttpStatusCode statusCode) => (int)statusCode is 301 or 302 or 303 or 307 or 308;

    private static bool IsCloudflareCookie(string name)
    {
        return string.Equals(name, "cf_clearance", StringComparison.Ordinal) ||
            name.StartsWith("__cf", StringComparison.Ordinal);
    }

    private static bool SameAuthority(Uri a, Uri b)
    {
        return Uri.Compare(
            a,
            b,
            UriComponents.SchemeAndServer,
            UriFormat.Unescaped,
            StringComparison.OrdinalIgnoreCase) == 0;
    }

    private static CardigannLoginOutcome SuccessfulOutcome(string? message = null)
    {
        return new() { Success = true, Message = message };
    }

    private static CardigannLoginOutcome FailedOutcome(string message, ConfigurationFailureKind kind)
    {
        return new() { Success = false, Message = message, FailureKind = kind };
    }

    private static Dictionary<string, string> RenderInputs(
        IReadOnlyDictionary<string, string>? inputs,
        IReadOnlyDictionary<string, object?> variables)
    {
        var rendered = new Dictionary<string, string>();
        if (inputs is null)
        {
            return rendered;
        }

        foreach (var (key, template) in inputs)
        {
            if (string.Equals(key, "oneurl", StringComparison.Ordinal))
            {
                continue;
            }

            rendered[key] = template.RenderCardigannTemplate(variables);
        }

        return rendered;
    }

    private static string BuildQueryString(IReadOnlyDictionary<string, string> inputs)
    {
        if (inputs.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            "&",
            inputs.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    private static Uri AppendQuery(Uri uri, string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return uri;
        }

        var separator = uri.Query.Length > 0 ? "&" : "?";
        return new Uri(uri.AbsoluteUri + separator + query);
    }

    public void InvalidateSession(string sessionKey)
    {
        if (state.Sessions.TryGetValue(sessionKey, out var session))
        {
            session.Authenticated = false;
            session.ClearanceUserAgent = null;
            session.Cookies = new CookieContainer();
            session.ClearTlsBinding();
        }
    }

    public bool IsLoggedOut(LoginBlock login, HttpStatusCode statusCode, string body)
    {
        if ((int)statusCode is 401 or 403)
        {
            return true;
        }

        if (login.Test?.Selector is null || string.IsNullOrEmpty(body))
        {
            return false;
        }

        using var document = _htmlParser.ParseDocument(body);
        return document.QuerySelector(login.Test.Selector) is null &&
            document.QuerySelector("input[type=password]") is not null;
    }

    public async Task<HttpResponseMessage> SendAsync(
        string sessionKey,
        Uri baseUri,
        HttpMethod method,
        Uri requestUri,
        string? formBody,
        string userAgent,
        bool captureCookies,
        CancellationToken cancellationToken)
    {
        using var content = formBody is null
            ? null
            : new StringContent(formBody, Encoding.UTF8, "application/x-www-form-urlencoded");
        return await SendCoreAsync(
            sessionKey,
            baseUri,
            method,
            requestUri,
            userAgent,
            headers: null,
            content,
            captureCookies,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<CardigannLoginOutcome> EnsureLoggedInAsync(
        string sessionKey,
        LoginBlock login,
        Uri baseUri,
        IReadOnlyDictionary<string, object?> variables,
        string? cookieValue,
        string userAgent,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(login);
        ArgumentNullException.ThrowIfNull(baseUri);

        if (login.Captcha is not null)
        {
            return FailedOutcome("captcha login not supported in this build.", ConfigurationFailureKind.Auth);
        }

        var session = state.Sessions.GetOrAdd(sessionKey, _ => new TrackerSession());
        if (!forceRefresh && session.Authenticated)
        {
            return SuccessfulOutcome();
        }

        var gate = state.LoginGates.GetOrAdd(sessionKey, _ => new SolverQueue { WaitForBackground = true });
        if (!await gate.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new SolverBusyException();
        }

        try
        {
            if (!forceRefresh && session.Authenticated)
            {
                return SuccessfulOutcome();
            }

            session.Authenticated = false;
            session.Cookies = new CookieContainer();
            session.ClearTlsBinding();

            CardigannLoginOutcome outcome;
            try
            {
                outcome = await PerformLoginAsync(
                    sessionKey,
                    session,
                    login,
                    baseUri,
                    variables,
                    cookieValue,
                    userAgent,
                    cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Cardigann login had a network error for {SessionKey}.", sessionKey);
                return FailedOutcome($"Network error during login: {ex.Message}", ConfigurationFailureKind.Fatal);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                return FailedOutcome($"Timed out during login: {ex.Message}", ConfigurationFailureKind.Transient);
            }

            if (outcome.Success)
            {
                session.Authenticated = true;
                logger.LogDebug("Cardigann login succeeded for {SessionKey}.", sessionKey);
            }
            else
            {
                logger.LogWarning("Cardigann login failed for {SessionKey}: {Reason}", sessionKey, outcome.Message);
            }

            return outcome;
        }
        finally
        {
            gate.Leave();
        }
    }

    private async Task<CardigannLoginOutcome> PerformLoginAsync(
        string sessionKey,
        TrackerSession session,
        LoginBlock login,
        Uri baseUri,
        IReadOnlyDictionary<string, object?> variables,
        string? cookieValue,
        string userAgent,
        CancellationToken cancellationToken)
    {
        var method = string.IsNullOrWhiteSpace(login.Method)
            ? MethodForm
            : login.Method.Trim().ToLowerInvariant();

        return method switch
        {
            MethodCookie => await LoginByCookieAsync(
                sessionKey,
                session,
                login,
                baseUri,
                cookieValue,
                userAgent,
                cancellationToken)
                .ConfigureAwait(false),
            MethodGet => await LoginByGetAsync(
                sessionKey,
                login,
                baseUri,
                variables,
                userAgent,
                cancellationToken)
                .ConfigureAwait(false),
            MethodOneUrl => await LoginByOneUrlAsync(
                sessionKey,
                login,
                baseUri,
                variables,
                userAgent,
                cancellationToken)
                .ConfigureAwait(false),
            MethodPost => await LoginByPostAsync(
                sessionKey,
                login,
                baseUri,
                variables,
                userAgent,
                cancellationToken)
                .ConfigureAwait(false),
            MethodForm => await LoginByFormAsync(
                sessionKey,
                login,
                baseUri,
                variables,
                userAgent,
                cancellationToken)
                .ConfigureAwait(false),
            _ => FailedOutcome($"Unsupported login method {method}.", ConfigurationFailureKind.Fatal),
        };
    }

    private async Task<CardigannLoginOutcome> LoginByCookieAsync(
        string sessionKey,
        TrackerSession session,
        LoginBlock login,
        Uri baseUri,
        string? cookieValue,
        string userAgent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cookieValue))
        {
            return FailedOutcome(
                "Cookie login requires a cookie value to be configured.",
                ConfigurationFailureKind.Auth);
        }

        foreach (var part in cookieValue.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var name = part[..separator].Trim();
            var value = part[(separator + 1)..].Trim();
            if (name.Length > 0)
            {
                try
                {
                    session.Cookies.Add(baseUri, new Cookie(name, value, "/"));
                }
                catch (CookieException)
                {
                    return FailedOutcome(
                        $"Configured cookie {name} is not a valid cookie.",
                        ConfigurationFailureKind.Auth);
                }
            }
        }

        if (session.Cookies.Count == 0)
        {
            return FailedOutcome(
                "Configured cookie value contained no name=value pairs.",
                ConfigurationFailureKind.Auth);
        }

        return await RunTestAsync(sessionKey, login, baseUri, userAgent, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CardigannLoginOutcome> LoginByGetAsync(
        string sessionKey,
        LoginBlock login,
        Uri baseUri,
        IReadOnlyDictionary<string, object?> variables,
        string userAgent,
        CancellationToken cancellationToken)
    {
        var inputs = RenderInputs(login.Inputs, variables);
        var query = BuildQueryString(inputs);
        var target = AppendQuery(new Uri(baseUri, login.Path ?? string.Empty), query);

        using var response = await SendCoreAsync(
            sessionKey,
            baseUri,
            HttpMethod.Get,
            target,
            userAgent,
            login.Headers,
            content: null,
            captureCookies: true,
            cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return CheckErrors(login, body)
            ?? await RunTestAsync(sessionKey, login, baseUri, userAgent, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CardigannLoginOutcome> LoginByOneUrlAsync(
        string sessionKey,
        LoginBlock login,
        Uri baseUri,
        IReadOnlyDictionary<string, object?> variables,
        string userAgent,
        CancellationToken cancellationToken)
    {
        var oneUrl = string.Empty;
        if (login.Inputs is not null && login.Inputs.TryGetValue("oneurl", out var template))
        {
            oneUrl = template.RenderCardigannTemplate(variables);
        }

        var target = new Uri(baseUri, (login.Path ?? string.Empty) + oneUrl);

        using var response = await SendCoreAsync(
            sessionKey,
            baseUri,
            HttpMethod.Get,
            target,
            userAgent,
            login.Headers,
            content: null,
            captureCookies: true,
            cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return CheckErrors(login, body)
            ?? await RunTestAsync(sessionKey, login, baseUri, userAgent, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CardigannLoginOutcome> LoginByPostAsync(
        string sessionKey,
        LoginBlock login,
        Uri baseUri,
        IReadOnlyDictionary<string, object?> variables,
        string userAgent,
        CancellationToken cancellationToken)
    {
        var inputs = RenderInputs(login.Inputs, variables);
        var target = new Uri(baseUri, login.Path ?? string.Empty);

        using var content = new FormUrlEncodedContent(inputs);
        using var response = await SendCoreAsync(
            sessionKey,
            baseUri,
            HttpMethod.Post,
            target,
            userAgent,
            login.Headers,
            content,
            captureCookies: true,
            cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return CheckErrors(login, body)
            ?? await RunTestAsync(sessionKey, login, baseUri, userAgent, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CardigannLoginOutcome> LoginByFormAsync(
        string sessionKey,
        LoginBlock login,
        Uri baseUri,
        IReadOnlyDictionary<string, object?> variables,
        string userAgent,
        CancellationToken cancellationToken)
    {
        var landingUri = new Uri(baseUri, login.Path ?? string.Empty);
        using var landingResponse = await SendCoreAsync(
            sessionKey,
            baseUri,
            HttpMethod.Get,
            landingUri,
            userAgent,
            login.Headers,
            content: null,
            captureCookies: true,
            cancellationToken)
            .ConfigureAwait(false);
        var landingBody = await landingResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        using var document = await _htmlParser.ParseDocumentAsync(landingBody, cancellationToken).ConfigureAwait(false);
        var formElement = document.QuerySelector(string.IsNullOrWhiteSpace(login.Form) ? "form" : login.Form);

        var formData = new Dictionary<string, string>();
        if (formElement is not null)
        {
            foreach (var input in formElement.QuerySelectorAll("input"))
            {
                var name = input.GetAttribute("name");
                if (!string.IsNullOrEmpty(name))
                {
                    formData[name] = input.GetAttribute("value") ?? string.Empty;
                }
            }
        }

        foreach (var pair in RenderInputs(login.Inputs, variables))
        {
            formData[pair.Key] = pair.Value;
        }

        if (login.SelectorInputs is not null)
        {
            foreach (var (name, selector) in login.SelectorInputs)
            {
                var value = resultParser.ExtractField(
                    document.DocumentElement,
                    selector,
                    $"login.selectorInputs.{name}");
                if (value is not null)
                {
                    formData[name] = value;
                }
            }
        }

        var getSelectorQuery = new Dictionary<string, string>();
        if (login.GetSelectorInputs is not null)
        {
            foreach (var (name, selector) in login.GetSelectorInputs)
            {
                var value = resultParser.ExtractField(
                    document.DocumentElement,
                    selector,
                    $"login.getSelectorInputs.{name}");
                if (value is not null)
                {
                    getSelectorQuery[name] = value;
                }
            }
        }

        var action = formElement?.GetAttribute("action");
        var submitPath = login.SubmitPath ?? (string.IsNullOrEmpty(action) ? login.Path : action) ?? string.Empty;
        var submitUri = AppendQuery(new Uri(baseUri, submitPath), BuildQueryString(getSelectorQuery));

        using var content = new FormUrlEncodedContent(formData);
        using var response = await SendCoreAsync(
            sessionKey,
            baseUri,
            HttpMethod.Post,
            submitUri,
            userAgent,
            login.Headers,
            content,
            captureCookies: true,
            cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return CheckErrors(login, body)
            ?? await RunTestAsync(sessionKey, login, baseUri, userAgent, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CardigannLoginOutcome> RunTestAsync(
        string sessionKey,
        LoginBlock login,
        Uri baseUri,
        string userAgent,
        CancellationToken cancellationToken)
    {
        if (login.Test is null)
        {
            return SuccessfulOutcome();
        }

        var testUri = new Uri(baseUri, login.Test.Path ?? string.Empty);
        using var response = await SendCoreAsync(
            sessionKey,
            baseUri,
            HttpMethod.Get,
            testUri,
            userAgent,
            login.Headers,
            content: null,
            captureCookies: true,
            cancellationToken)
            .ConfigureAwait(false);
        var status = (int)response.StatusCode;
        if (status is 401 or 403)
        {
            return FailedOutcome(
                "Login verification was rejected (authentication required).",
                ConfigurationFailureKind.Auth);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(login.Test.Selector))
        {
            using var document = await _htmlParser.ParseDocumentAsync(body, cancellationToken).ConfigureAwait(false);
            if (document.QuerySelector(login.Test.Selector) is null)
            {
                return FailedOutcome(
                    "Login verification selector did not match; credentials may be incorrect.",
                    ConfigurationFailureKind.Auth);
            }

            return SuccessfulOutcome("Logged in.");
        }

        return response.IsSuccessStatusCode
            ? SuccessfulOutcome("Logged in.")
            : FailedOutcome($"Login verification returned HTTP {status}.", ConfigurationFailureKind.Auth);
    }

    private CardigannLoginOutcome? CheckErrors(LoginBlock login, string body)
    {
        if (login.Error is null || login.Error.Count == 0 || string.IsNullOrEmpty(body))
        {
            return null;
        }

        using var document = _htmlParser.ParseDocument(body);
        foreach (var error in login.Error)
        {
            if (string.IsNullOrWhiteSpace(error.Selector))
            {
                continue;
            }

            var matched = document.QuerySelector(error.Selector);
            if (matched is null)
            {
                continue;
            }

            var message = error.Message is not null
                ? resultParser.ExtractField(document.DocumentElement, error.Message, "login.error.message")
                : null;
            message ??= matched.TextContent?.Trim();
            return FailedOutcome(
                string.IsNullOrWhiteSpace(message) ? "Login rejected by the tracker." : message,
                ConfigurationFailureKind.Auth);
        }

        return null;
    }

    private string? BuildCookieHeader(string sessionKey, Uri requestUri)
    {
        return state.Sessions.TryGetValue(sessionKey, out var session)
            ? session.Cookies.GetCookieHeader(requestUri)
            : null;
    }

    private void CaptureCookies(string sessionKey, Uri requestUri, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            return;
        }

        var session = state.Sessions.GetOrAdd(sessionKey, _ => new TrackerSession());
        foreach (var raw in setCookies)
        {
            try
            {
                session.Cookies.SetCookies(requestUri, raw);
            }
            catch (CookieException ex)
            {
                logger.LogWarning(ex, "The tracker returned an invalid cookie for {Host}.", requestUri.Host);
            }
        }
    }

    private async Task<HttpResponseMessage> SendCoreAsync(
        string sessionKey,
        Uri cookieScope,
        HttpMethod method,
        Uri requestUri,
        string userAgent,
        IReadOnlyDictionary<string, List<string>>? headers,
        HttpContent? content,
        bool captureCookies,
        CancellationToken cancellationToken)
    {
        var client = clientFactory.CreateClient(HttpClientName);
        var contentType = content?.Headers.ContentType;

        var body = content is null
            ? null
            : await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var currentMethod = method;
        var currentUri = requestUri;
        var currentBody = body;
        var clearanceRetried = false;
        var clearanceApplied = false;
        var clearanceReused = false;
        var clearanceRefreshed = false;
        for (var hop = 0; ; hop++)
        {
            var onCookieScope = SameAuthority(currentUri, cookieScope);
            using var request = new HttpRequestMessage(currentMethod, currentUri);
            if (onCookieScope && sessionKey.LastIndexOf('#') is var separator && separator >= 0)
            {
                request.Options.Set(AuthenticationScopeKey, sessionKey[(separator + 1)..]);
            }

            request.Headers.TryAddWithoutValidation(
                "User-Agent",
                ResolveUserAgent(sessionKey, userAgent, onCookieScope));
            request.Headers.TryAddWithoutValidation("Accept", AcceptHeader);
            var cookieHeader = BuildCookieHeader(sessionKey, currentUri);
            if (!string.IsNullOrEmpty(cookieHeader) && onCookieScope)
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            }

            if (headers is not null && onCookieScope)
            {
                foreach (var (name, values) in headers)
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        request.Headers.TryAddWithoutValidation(name, values);
                    }
                }
            }

            if (currentBody is not null)
            {
                request.Content = new ByteArrayContent(currentBody);
                request.Content.Headers.ContentType = contentType;
            }

            var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
            if (captureCookies && onCookieScope)
            {
                CaptureCookies(sessionKey, currentUri, response);
            }

            if (hop < MaxRedirects && IsRedirect(response.StatusCode) && response.Headers.Location is not null)
            {
                currentUri = new Uri(currentUri, response.Headers.Location);
                currentMethod = HttpMethod.Get;
                currentBody = null;
                response.Dispose();
                continue;
            }

            if (onCookieScope && response.DetectCloudflareChallenge() is not null)
            {
                try
                {
                    var session = state.Sessions.GetOrAdd(sessionKey, _ => new TrackerSession());
                    if (clearanceReused && !clearanceRefreshed)
                    {
                        solver.Forget(cookieScope);
                        clearanceRetried = false;
                        clearanceReused = false;
                        clearanceRefreshed = true;
                        logger.LogDebug(
                            "The tracker of {SessionKey} refused Cardigann's stored clearance cookie.",
                            sessionKey);
                    }

                    if (!clearanceRetried && !session.ClearanceIsTlsBound(timeProvider.GetUtcNow()))
                    {
                        clearanceRetried = true;
                        (clearanceApplied, clearanceReused) =
                            await TryAcquireClearanceAsync(sessionKey, cookieScope, cancellationToken)
                                .ConfigureAwait(false);
                        if (clearanceApplied)
                        {
                            response.Dispose();
                            continue;
                        }
                    }

                    if (currentMethod == HttpMethod.Get)
                    {
                        if (clearanceApplied && !clearanceReused)
                        {
                            session.BindClearanceToTls(timeProvider.GetUtcNow() + TlsBindingLifetime);
                            logger.LogDebug("The tracker of {SessionKey} rejected the clearance cookie.", sessionKey);
                        }

                        if (await FetchThroughSolverAsync(currentUri,
                            request.Options.TryGetValue(AuthenticationScopeKey, out var scope) ? scope : null,
                            cancellationToken).ConfigureAwait(false)
                            is { } solved)
                        {
                            response.Dispose();
                            return solved;
                        }
                    }
                }
                catch
                {
                    response.Dispose();
                    throw;
                }
            }

            return response;
        }
    }

    private async Task<HttpResponseMessage?> FetchThroughSolverAsync(
        Uri requestUri,
        string? authenticationScope,
        CancellationToken cancellationToken)
    {
        var solved = await solver.SolveAsync(requestUri, reuse: false, cancellationToken, authenticationScope)
            .ConfigureAwait(false);
        if (solved?.Body is not { Length: > 0 } solvedBody || solved.Status is < 200 or > 299)
        {
            return null;
        }

        var unwrapped = UnwrapSolvedPayload(solvedBody);
        return new HttpResponseMessage((HttpStatusCode)solved.Status)
        {
            Content = new StringContent(
                unwrapped ?? solvedBody,
                Encoding.UTF8,
                unwrapped is null ? "text/html" : "application/json"),
        };
    }

    private string? UnwrapSolvedPayload(string solvedBody)
    {
        using var document = _htmlParser.ParseDocument(solvedBody);
        if (document.Body?.QuerySelectorAll("pre") is not { Length: 1 } preformatted)
        {
            return null;
        }

        var payload = preformatted[0].TextContent.Trim();
        return payload.StartsWith('{') || payload.StartsWith('[') ? payload : null;
    }

    private async Task<(bool Applied, bool Reused)> TryAcquireClearanceAsync(
        string sessionKey,
        Uri scopeUri,
        CancellationToken cancellationToken)
    {
        var session = state.Sessions.GetOrAdd(sessionKey, _ => new TrackerSession());
        var stamp = session.ClearanceStamp;
        if (!await session.ClearanceGate.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new SolverBusyException();
        }

        try
        {
            if (session.ClearanceStamp != stamp)
            {
                return (true, true);
            }

            var authenticationScope = sessionKey.LastIndexOf('#') is var separator && separator >= 0
                ? sessionKey[(separator + 1)..] : null;
            var clearance = await solver.SolveAsync(scopeUri, reuse: true, cancellationToken, authenticationScope)
                .ConfigureAwait(false);
            if (clearance is null)
            {
                return (false, false);
            }

            var applied = 0;
            foreach (var (name, value) in clearance.Cookies)
            {
                if (!IsCloudflareCookie(name))
                {
                    continue;
                }

                try
                {
                    session.Cookies.Add(scopeUri, new Cookie(name, value, "/"));
                    applied++;
                }
                catch (CookieException exception)
                {
                    logger.LogWarning(exception, "The solver returned an invalid cookie for {Host}.", scopeUri.Host);
                }
            }

            if (applied == 0)
            {
                logger.LogWarning(
                    "FlareSolverr returned no Cloudflare cookie for the unsolved challenge of {SessionKey}.",
                    sessionKey);

                session.BindClearanceToTls(timeProvider.GetUtcNow() + TlsBindingLifetime);
                return (false, false);
            }

            session.ClearanceUserAgent = clearance.UserAgent;
            session.ClearanceStamp++;
            logger.LogDebug(
                "FlareSolverr solved the Cloudflare challenge for {SessionKey} and kept {CookieCount} cookies.",
                sessionKey,
                applied);
            return (true, clearance.Reused);
        }
        finally
        {
            session.ClearanceGate.Leave();
        }
    }

    private string ResolveUserAgent(string sessionKey, string userAgent, bool onCookieScope)
    {
        return onCookieScope &&
            state.Sessions.TryGetValue(sessionKey, out var session) &&
            session.ClearanceUserAgent is { Length: > 0 } clearanceUserAgent
                ? clearanceUserAgent
                : userAgent;
    }
}
