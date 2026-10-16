using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Extensions.Wikidata.QueryEngine;

using Magpie.Sdk.Http;

namespace Magpie.Extensions.Wikidata;

internal sealed class WikidataClient(IHttpClientFactory httpClientFactory) : IWikidataClient
{
    public const string HttpClientName = "magpie.wikidata";

    public const string DefaultApiUrl = "https://www.wikidata.org/w/api.php";

    public const string DefaultSparqlUrl = "https://query.wikidata.org/sparql";

    private const int SparqlGetThreshold = 1800;

    private const int EntitySearchLimit = 50;

    private const int EntitySearchPageLimit = 10;

    private static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(20);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _requestGate = new(2, 2);

    public async Task<IReadOnlyDictionary<string, EntityDto>> GetEntitiesAsync(
        IReadOnlyCollection<string> ids,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<string, EntityDto>();
        }

        if (ids.Count > 50)
        {
            throw new InvalidOperationException("Wikidata entity batches cannot exceed 50 ids.");
        }

        var parameters = new List<(string Key, string Value)>
        {
            ("action", "wbgetentities"),
            ("ids", string.Join("|", ids)),
            ("props", "labels|descriptions|aliases|claims")
        };
        if (!context.FetchAllLanguages)
        {
            parameters.Add(("languages", string.Join("|", context.FallbackLanguages)));
            parameters.Add(("languagefallback", "1"));
        }

        parameters.AddRange(
        [
            ("redirects", "yes"),
            ("format", "json"),
            ("formatversion", "2"),
            ("maxlag", "5")
        ]);
        var envelope = await GetActionJsonAsync<EntitiesResponseDto>(
            BuildUri(DefaultApiUrl, parameters),
            context,
            cancellationToken).ConfigureAwait(false);
        if (envelope?.Entities is null)
        {
            throw new HttpRequestException("Wikidata returned no entity result.");
        }

        return envelope.Entities
            .Where(pair => pair.Value is not null && !IsMissing(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<string>> SearchEntitiesAsync(
        string search,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return [];
        }

        var language = context.FallbackLanguages.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? Settings.FallbackLanguage;
        var qids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var continuation = default(int?);
        var pages = 0;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pages++ == EntitySearchPageLimit)
            {
                throw new WikidataBudgetException("Wikidata entity search scanned too many pages without finishing.");
            }

            var parameters = new List<(string Key, string Value)>
            {
                ("action", "wbsearchentities"),
                ("search", search.Trim()),
                ("language", language),
                ("uselang", language),
                ("type", "item"),
                ("limit", EntitySearchLimit.ToString(System.Globalization.CultureInfo.InvariantCulture))
            };
            if (continuation is { } offset)
            {
                parameters.Add(("continue", offset.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            parameters.AddRange(
            [
                ("format", "json"),
                ("formatversion", "2"),
                ("maxlag", "5")
            ]);
            var envelope = await GetActionJsonAsync<EntitySearchResponseDto>(
                BuildUri(DefaultApiUrl, parameters),
                context,
                cancellationToken).ConfigureAwait(false);
            if (envelope?.Search is null)
            {
                throw new HttpRequestException("Wikidata returned no entity search result.");
            }

            foreach (var value in envelope.Search)
            {
                var qid = value.Id.ToQid();
                if (qid is not null && seen.Add(qid))
                {
                    if (qids.Count == EntitySearchLimit * EntitySearchPageLimit)
                    {
                        throw new WikidataBudgetException("Wikidata entity search matched too many results.");
                    }

                    qids.Add(qid);
                }
            }

            if (envelope.SearchContinue is { } next && continuation is { } current && next <= current)
            {
                throw new HttpRequestException("Wikidata returned a non-advancing entity search continuation.");
            }

            continuation = envelope.SearchContinue;
        }
        while (continuation is not null);

        return qids;
    }

    public async Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>> ExecuteSparqlAsync(
        SparqlRequest request,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var encoded = "query=" + Uri.EscapeDataString(request.QueryText) + "&format=json";
        var useGet = DefaultSparqlUrl.Length + 1 + encoded.Length <= SparqlGetThreshold;
        using var message = useGet
            ? new HttpRequestMessage(HttpMethod.Get, new Uri(DefaultSparqlUrl + "?" + encoded, UriKind.Absolute))
            : new HttpRequestMessage(HttpMethod.Post, new Uri(DefaultSparqlUrl, UriKind.Absolute))
            {
                Content = new FormUrlEncodedContent(
                [
                    new KeyValuePair<string, string>("query", request.QueryText),
                    new KeyValuePair<string, string>("format", "json")
                ])
            };
        AddHeaders(message, context, "application/sparql-results+json");

        var response = await SendAsync(message, context, request.Timeout, cancellationToken).ConfigureAwait(false);
        using (response)
        {
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var envelope = Deserialize<SparqlResponseDto>(body, "SPARQL");
            if (envelope?.Results?.Bindings is null)
            {
                throw new HttpRequestException("Wikidata returned no SPARQL result.");
            }

            return envelope.Results.Bindings;
        }
    }

    internal static async Task<bool> IsThrottledAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri?.AbsolutePath != "/w/api.php")
        {
            return false;
        }

        var body = await response.Content.ReadAsStringAsync()
            .WaitAsync(Timeout.InfiniteTimeSpan, TimeProvider.System, cancellationToken).ConfigureAwait(false);
        var error = ReadActionError(body);
        return string.Equals(error?.Code, "maxlag", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(error?.Code, "ratelimited", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMissing(EntityDto entity) => entity.Missing;

    private static void AddHeaders(HttpRequestMessage message, RequestContext context, string? accept)
    {
        message.Headers.TryAddWithoutValidation("User-Agent", context.UserAgent);
        if (!string.IsNullOrWhiteSpace(accept))
        {
            message.Headers.TryAddWithoutValidation("Accept", accept);
        }
    }

    private static ApiErrorDto? ReadActionError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new JsonException("Wikidata returned an empty Action API envelope.");
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("error", out var errorElement) ||
            errorElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return JsonSerializer.Deserialize<ApiErrorDto>(errorElement.GetRawText(), JsonOptions) ?? new ApiErrorDto();
    }

    private static T? Deserialize<T>(string body, string endpoint)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new JsonException("Wikidata returned an empty " + endpoint + " envelope.");
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new JsonException("Wikidata returned a malformed " + endpoint + " envelope.");
    }

    private static Uri BuildUri(string baseUrl, IReadOnlyList<(string Key, string Value)> parameters)
    {
        var builder = new StringBuilder(baseUrl);
        builder.Append('?');
        for (var index = 0; index < parameters.Count; index++)
        {
            if (index > 0)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(parameters[index].Key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(parameters[index].Value));
        }

        return new Uri(builder.ToString(), UriKind.Absolute);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage message,
        RequestContext context,
        TimeSpan requestTimeout,
        CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            message.SetTransportTimeout(requestTimeout);
            using var client = httpClientFactory.CreateClient(HttpClientName);
            message.Properties[WikidataHttpHandler.RequestObserver] = context.UpstreamRequestObserver;
            return await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task<T?> GetActionJsonAsync<T>(
        Uri uri,
        RequestContext context,
        CancellationToken cancellationToken)
        where T : class
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, uri);
        AddHeaders(message, context, accept: null);
        using var response = await SendAsync(message, context, ActionTimeout, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var error = ReadActionError(body);
        if (error is not null)
        {
            throw new HttpRequestException("Wikidata Action API returned an error.");
        }

        return Deserialize<T>(body, "Action API");
    }
}
