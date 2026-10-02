using System.Collections.Generic;
using System.Linq;

using Magpie.Models;
using Magpie.Models.Integrations;
using Magpie.Sdk.Api;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Notifications;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class ProvidersEndpointGroup : IApiEndpointGroup
{
    public string Name => "providers";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetProviders)
            .WithName(nameof(GetProviders))
            .Produces<IReadOnlyList<ProviderDescriptor>>()
            .RequireViewer();
    }

    internal IResult GetProviders(
        [FromServices] IEnumerable<IIndexerIntegration> indexers,
        [FromServices] IEnumerable<IDownloadClientIntegration> downloadClients,
        [FromServices] IEnumerable<IMediaServerIntegration> mediaServers,
        [FromServices] IEnumerable<INotificationProvider> notifications,
        [FromServices] IEnumerable<IIndexerFeedProvider> indexerFeeds)
    {
        var providers = indexers.Select(p => Describe("indexer", p.ProviderType, p.DisplayName, p.SettingsSchema))
            .Concat(downloadClients.Select(p => Describe(
                "downloadClient",
                p.ProviderType,
                p.DisplayName,
                p.SettingsSchema)))
            .Concat(mediaServers.Select(p => Describe("mediaServer", p.ProviderType, p.DisplayName, p.SettingsSchema)))
            .Concat(notifications.Select(p => Describe(
                "notification",
                p.ProviderType,
                p.DisplayName,
                p.SettingsSchema)))
            .Concat(indexerFeeds.Select(p => Describe("indexerFeed", p.ProviderType, p.DisplayName, p.SettingsSchema)))
            .OrderBy(provider => provider.Kind)
            .ThenBy(provider => provider.ProviderType)
            .ToList();

        return Results.Ok(providers);

        static ProviderDescriptor Describe(
            string kind,
            string providerType,
            string displayName,
            IReadOnlyList<SettingSchemaDefinition> schema) =>
            new() { Kind = kind, ProviderType = providerType, DisplayName = displayName, Schema = schema };
    }
}
