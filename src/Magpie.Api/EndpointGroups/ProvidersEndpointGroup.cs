using System.Collections.Generic;
using System.Linq;

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
        var providers = indexers.Select(provider => new ProviderDescriptor
        {
            Kind = "indexer",
            ProviderType = provider.ProviderType,
            Schema = provider.SettingsSchema
        })
            .Concat(downloadClients.Select(provider => new ProviderDescriptor
            {
                Kind = "downloadClient",
                ProviderType = provider.ProviderType,
                Schema = provider.SettingsSchema
            }))
            .Concat(mediaServers.Select(provider => new ProviderDescriptor
            {
                Kind = "mediaServer",
                ProviderType = provider.ProviderType,
                Schema = provider.SettingsSchema
            }))
            .Concat(notifications.Select(provider => new ProviderDescriptor
            {
                Kind = "notification",
                ProviderType = provider.ProviderType,
                Schema = provider.SettingsSchema
            }))
            .Concat(indexerFeeds.Select(provider => new ProviderDescriptor
            {
                Kind = "indexerFeed",
                ProviderType = provider.ProviderType,
                Schema = provider.SettingsSchema
            }))
            .OrderBy(provider => provider.Kind)
            .ThenBy(provider => provider.ProviderType)
            .ToList();

        return Results.Ok(providers);
    }
}
