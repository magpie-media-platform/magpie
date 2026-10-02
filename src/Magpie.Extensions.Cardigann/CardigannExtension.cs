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
using Magpie.Sdk.Configuration;
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
    public Guid Id => Constants.ExtensionId;

    /// <inheritdoc />
    public string Name => Constants.DisplayName;

    private static IndexerFeed BuildDefaultFeed()
    {
        return new IndexerFeed
        {
            Id = Guid.NewGuid(),
            ProviderType = Constants.ProviderTypeName,
            Name = Constants.DefaultFeedName,
            Description = DefaultFeedDescription,
            Settings = new Dictionary<string, string>
            {
                [Constants.GitUrlSettingKey] = Constants.DefaultGitUrl,
                [Constants.BranchSettingKey] = Constants.DefaultBranch,
                [Constants.DefinitionsPathSettingKey] = Constants.DefaultDefinitionsPath,
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
            .AddUpstreamCooldown(request => request.Options.TryGetValue(
                CardigannSessionManager.AuthenticationScopeKey,
                out var scope) ? scope : null)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                UseCookies = false,
                AllowAutoRedirect = false
            });
        services.AddHttpClient(Git.GitCoreDefinitionSource.HttpClientName);
        services
            .AddHttpClient(
                FlareSolverrClient.HttpClientName,
                client => client.Timeout = FlareSolverrClient.RequestTimeout)
            .ConfigureAdditionalHttpMessageHandlers((handlers, _) => handlers.Clear());

        services.AddSingleton<ICardigannLoader, CardigannLoader>();
        services.AddSingleton<IResultParser, ResultParser>();
        services.AddSingleton<SolverQueue>();
        services.AddScoped<ICloudflareSolver, FlareSolverrClient>();

        services.AddSingleton<CardigannSessionState>();
        services.AddScoped<ICardigannSessionManager, CardigannSessionManager>();
        services.AddSingleton<IExtensionSettingsSchemaProvider, CardigannSettingsSchemaProvider>();
        services.AddSingleton<Git.GitCoreDefinitionSource>();
        services.AddSingleton<IIndexerFeedProvider, CardigannFeedProvider>();
        services.AddScoped<IRuntimeIndexerIntegrationFactory, CardigannRuntimeIntegrationFactory>();

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
        if (feeds.Any(feed => feed.ProviderType == Constants.ProviderTypeName))
        {
            return;
        }

        await indexerService
            .CreateFeedAsync(BuildDefaultFeed(), actingUserId: null, cancellationToken)
            .ConfigureAwait(false);

        var logger = context.LoggerFactory.CreateLogger<CardigannExtension>();
        logger.LogInformation(
            "Cardigann extension registered the default indexer feed for provider type {ProviderType}",
            Constants.ProviderTypeName);

        await jobManager
            .ExecuteAsync(new JobEnqueueRequest { JobType = "IndexerFeedSyncScan" }, cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation("Cardigann extension enqueued an indexer feed sync after it created the default feed");
    }
}
