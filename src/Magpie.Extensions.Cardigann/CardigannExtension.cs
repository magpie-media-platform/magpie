using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Cardigann.Engine;
using Magpie.Extensions.Cardigann.Engine.Session;
using Magpie.Models.Data;
using Magpie.Models.Jobs;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Releases;
using Magpie.Sdk.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Cardigann;

/// <summary>
/// The Magpie extension for Cardigann indexer definitions.
/// </summary>
public sealed class CardigannExtension : IExtensionLifecycle
{
    private const string DefaultFeedDescription =
        "Community Cardigann tracker definitions synced from the default Jackett remote.";

    /// <inheritdoc />
    public Guid Id { get; } = new("8B0F2A4C-1F3E-4A55-9E8E-2A6B9F1F0002");

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public string Name => "Cardigann";

    /// <inheritdoc />
    public string? TargetABI => "[1.0.0,)";

    private static IndexerFeed BuildDefaultFeed()
    {
        return new IndexerFeed
        {
            Id = Guid.NewGuid(),
            ProviderType = CardigannFeedProvider.ProviderTypeName,
            Name = CardigannFeedProvider.DefaultFeedName,
            Description = DefaultFeedDescription,
            Settings = new Dictionary<string, string>
            {
                [CardigannFeedProvider.GitUrlSettingKey] = CardigannFeedProvider.DefaultGitUrl,
                [CardigannFeedProvider.BranchSettingKey] = CardigannFeedProvider.DefaultBranch,
                [CardigannFeedProvider.DefinitionsPathSettingKey] = CardigannFeedProvider.DefaultDefinitionsPath,
            },
            Enabled = true,
        };
    }

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IReleaseComparer, CardigannReleaseComparer>();

        services
            .AddHttpClient(CardigannSessionManager.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                UseCookies = false,
                AllowAutoRedirect = false
            });
        services.AddHttpClient(Git.GitCoreDefinitionSource.HttpClientName);

        services.AddSingleton<ICardigannLoader, CardigannLoader>();
        services.AddSingleton<IResultParser, ResultParser>();
        services.AddSingleton<ICardigannSessionManager, CardigannSessionManager>();
        services.AddSingleton<Git.IGitDefinitionSource, Git.GitCoreDefinitionSource>();
        services.AddSingleton<IIndexerFeedProvider, CardigannFeedProvider>();
        services.AddSingleton<IRuntimeIndexerIntegrationFactory, CardigannRuntimeIntegrationFactory>();

        services.AddScoped<IDiagnosticCheck, CardigannDuplicateFeedDiagnostic>();
        services.AddScoped<IDiagnosticRepair, CardigannDuplicateFeedRepair>();
    }

    /// <inheritdoc />
    public async Task OnExtensionInstallAsync(
        ExtensionLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var indexerService = context.ServiceProvider.GetRequiredService<IIndexerService>();
        var jobManager = context.ServiceProvider.GetRequiredService<IJobManager>();

        var feeds = await indexerService.ListFeedsAsync(cancellationToken).ConfigureAwait(false);
        if (feeds.Any(feed => feed.ProviderType == CardigannFeedProvider.ProviderTypeName))
        {
            return;
        }

        await indexerService
            .CreateFeedAsync(BuildDefaultFeed(), actingUserId: null, cancellationToken)
            .ConfigureAwait(false);

        var logger = context.LoggerFactory.CreateLogger<CardigannExtension>();
        logger.LogInformation(
            "Cardigann extension registered the default indexer feed for provider type {ProviderType}",
            CardigannFeedProvider.ProviderTypeName);

        await jobManager
            .ExecuteAsync(new JobEnqueueRequest { JobType = "IndexerFeedSyncScan" }, cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation("Cardigann extension enqueued an indexer feed sync after it created the default feed");
    }
}
