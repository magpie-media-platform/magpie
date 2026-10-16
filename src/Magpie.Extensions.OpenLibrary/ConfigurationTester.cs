using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary.QueryEngine;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Sdk.Configuration;

namespace Magpie.Extensions.OpenLibrary;

internal sealed class ConfigurationTester(IOpenLibraryClient client) : IExtensionConfigurationTester
{
    private static readonly ApiRequest Probe = new()
    {
        Kind = ApiRequestKind.Search,
        Path = "search.json",
        Parameters =
        [
            new ApiRequestParameter("q", "key:\"/works/OL45883W\""),
            new ApiRequestParameter("fields", "key"),
            new ApiRequestParameter("limit", "1"),
            new ApiRequestParameter("offset", "0")
        ],
        CacheKey = "search.json?fields=key&limit=1&offset=0&q=key:\"/works/OL45883W\""
    };

    public Guid ExtensionId => Constants.ExtensionId;

    public string SettingsGroupName => ProviderSettings.GroupName;

    public async Task<ConfigurationTestResult> TestConfigurationAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        var contact = settings
            .FirstOrDefault(pair =>
                string.Equals(pair.Key, ProviderSettings.ContactKey, StringComparison.OrdinalIgnoreCase))
            .Value;
        var context = RequestContext.ForContact(contact);
        try
        {
            var status = await client.GetStatusAsync(Probe, context, cancellationToken).ConfigureAwait(false);
            return (int)status is >= 200 and < 300
                ? new ConfigurationTestResult { Success = true, Message = "Connected to Open Library." }
                : new ConfigurationTestResult
                {
                    Success = false,
                    Message = string.Format(
                        CultureInfo.InvariantCulture,
                        "Open Library returned HTTP {0}.",
                        (int)status),
                    FailureKind = ResolveFailureKind(status)
                };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            return new ConfigurationTestResult
            {
                Success = false,
                Message = ex.Message,
                FailureKind = ConfigurationFailureKind.Transient
            };
        }
        catch (HttpRequestException ex)
        {
            return new ConfigurationTestResult
            {
                Success = false,
                Message = string.Concat("Could not reach Open Library: ", ex.Message),
                FailureKind = ConfigurationFailureKind.Fatal
            };
        }
    }

    private ConfigurationFailureKind ResolveFailureKind(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ConfigurationFailureKind.Auth,
            HttpStatusCode.NotFound => ConfigurationFailureKind.NotFound,
            HttpStatusCode.RequestTimeout => ConfigurationFailureKind.Transient,
            _ when (int)statusCode >= 500 => ConfigurationFailureKind.Transient,
            _ => ConfigurationFailureKind.Fatal
        };
    }
}
