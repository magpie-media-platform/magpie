using System;

using Magpie.Sdk.Content;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Media;
using Magpie.Sdk.Notifications;

using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Extensions.IntegrationTestShims;

/// <summary>
/// The Magpie extension for mock integrations in the end-to-end test harness.
/// </summary>
public sealed class IntegrationTestShimsExtension : IExtension
{
    private static readonly Guid ExtensionId = new("D6E20B46-39EF-4AFB-8B85-FABCE83F4C25");

    /// <inheritdoc />
    public Guid Id => ExtensionId;

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public string Name => "Integration Test Shims";

    /// <inheritdoc />
    public string? TargetABI => "[1.0.0,)";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddSingleton<IDownloadClientIntegration, DownloadClient.MockDownloadClientIntegration>();
        services.AddSingleton<IIndexerIntegration, Indexer.MockIndexerIntegration>();
        services.AddSingleton<IIndexerFeedProvider, Indexer.MockIndexerFeedProvider>();

        services.AddSingleton<IMediaServerIntegration, MediaServer.MockMediaServerIntegration>();

        services.AddSingleton<IMediaInfoExtractor, MediaInfo.MockMediaInfoExtractor>();

        services.AddSingleton<INotificationProvider, Notification.MockNotificationProvider>();

        services.AddSingleton<ISubjectMetadataProvider, SubjectMetadata.MockSubjectMetadataProvider>();

        services.AddSingleton(
            _ => new HierarchyMetadata.MockHierarchyMetadataAdvancedSearcher());
        services.AddSingleton<IContentHierarchyMetadataSearcher>(
            provider => provider.GetRequiredService<HierarchyMetadata.MockHierarchyMetadataAdvancedSearcher>());
        services.AddSingleton<IContentHierarchyMetadataProvider>(
            _ => new HierarchyMetadata.MockHierarchyMetadataProvider(
                HierarchyMetadata.MockHierarchyMetadataProviderType.Primary));
        services.AddSingleton<IContentHierarchyMetadataProvider>(
            _ => new HierarchyMetadata.MockHierarchyMetadataProvider(
                HierarchyMetadata.MockHierarchyMetadataProviderType.Secondary));
    }
}
