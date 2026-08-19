using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Database;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Api;
using Magpie.App;
using Magpie.Core.Config;
using Magpie.Core.Extensions;
using Magpie.Core.Jobs;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using PostgreSqlDbContextFactory = Magpie.Data.PostgreSQL.MagpieDbContextFactory;
using SqliteDbContextFactory = Magpie.Data.SQLite.MagpieDbContextFactory;
using SqlServerDbContextFactory = Magpie.Data.SqlServer.MagpieDbContextFactory;

#pragma warning disable IDE0130
namespace Microsoft.AspNetCore.Builder;
#pragma warning restore IDE0130

/// <summary>
/// The host builder extensions that configure a Magpie application.
/// </summary>
public static class MagpieHostBuilderExtensions
{
    private const string DatabaseProviderKey = "Magpie:Database:Provider";

    private const string DatabaseConnectionStringKey = "Magpie:Database:ConnectionString";

    private const string CommandLineLoggingKey = "Magpie:CommandLineLogging";

    private static IReadOnlyList<IExtension> BuiltInExtensions => [new MagpieCoreApiExtension()];

    /// <summary>
    /// Adds Magpie services to a host application builder.
    /// </summary>
    /// <param name="builder">The host application builder to configure.</param>
    public static IHostApplicationBuilder AddMagpie(this IHostApplicationBuilder builder)
    {
        return AddMagpie(builder, startHostedServices: true);
    }

    /// <summary>
    /// Adds Magpie services for command-line hosting.
    /// </summary>
    /// <param name="builder">The host application builder to configure.</param>
    public static IHostApplicationBuilder AddMagpieCli(this IHostApplicationBuilder builder)
    {
        builder.Properties[CommandLineLoggingKey] = true;
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        return AddMagpie(builder, startHostedServices: false);
    }

    /// <summary>
    /// Adds the Magpie system host environment.
    /// </summary>
    /// <param name="builder">The web application builder to configure.</param>
    /// <param name="hostStatus">The current host status.</param>
    public static WebApplicationBuilder AddMagpieSystem(this WebApplicationBuilder builder, HostStatusInfo hostStatus)
    {
        if (hostStatus.Mode == HostMode.WindowsService)
        {
            builder.Services.AddWindowsService();
        }
        else if (hostStatus.Mode == HostMode.Systemd)
        {
            builder.Services.AddSystemd();
        }

        var options = builder.Configuration.GetSection(MagpieHostOptions.SectionName).Get<MagpieHostOptions>()
            ?? new MagpieHostOptions();

        var contentRoot = builder.Environment.ContentRootPath;
        var extensionsRoot = ResolveExtensionsRoot(options.Extensions.Path, contentRoot);

        var hostEnvironment = new MagpieHostEnvironment(
            NodeId: string.IsNullOrWhiteSpace(options.Cluster.NodeId)
                ? Environment.MachineName
                : options.Cluster.NodeId,
            IsClustered: options.Cluster.Mode != ExtensionClusterMode.SingleNode,
            IsSupervised: hostStatus.Mode is HostMode.Systemd or HostMode.WindowsService or HostMode.Sentinel,
            SystemRoot: contentRoot,
            ExtensionsRoot: extensionsRoot,
            ConfigRoot: Path.Combine(contentRoot, "config"),
            DataRoot: ResolveDataRoot(options.Data.Path, contentRoot),
            StartedAt: DateTime.UtcNow);

        builder.Services.AddSingleton(hostStatus);
        builder.Services.AddSingleton(hostEnvironment);

        return builder;
    }

    internal static async Task MigrateMagpieDatabaseAsync(
        this IHostApplicationBuilder builder,
        CancellationToken cancellationToken = default)
    {
        using var loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
            ConfigureBootstrapConsoleLogging(builder, logging);
        });

        var logger = loggerFactory.CreateLogger(nameof(MagpieHostBuilderExtensions));

        await PrepareDatabaseAsync(builder, logger, cancellationToken).ConfigureAwait(false);

        var context = CreateBootstrapDbContext(builder.Configuration, builder.Environment.ContentRootPath);
        await using var contextHandle = context.ConfigureAwait(false);

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task BackfillMediaFilePathKeysAsync(
        IHostApplicationBuilder builder,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var context = CreateBootstrapDbContext(builder.Configuration, builder.Environment.ContentRootPath);
        await using var contextHandle = context.ConfigureAwait(false);
        if (!await context.MediaFiles
            .AnyAsync(mediaFile => mediaFile.PathKey.StartsWith("legacy:"), cancellationToken)
            .ConfigureAwait(false))
        {
            return;
        }

        var services = builder.Services.BuildServiceProvider();
        await using var serviceHandle = services.ConfigureAwait(false);
        var scope = services.CreateAsyncScope();
        await using var scopeHandle = scope.ConfigureAwait(false);
        var contentTypeRegistry = scope.ServiceProvider.GetRequiredService<IContentTypeRegistry>();
        var fileSystem = scope.ServiceProvider.GetRequiredService<IFileSystem>();
        var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transactionHandle = transaction.ConfigureAwait(false);
        var mediaFiles = await context.MediaFiles
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var duplicates = new List<MediaFile>();
        foreach (var group in mediaFiles.GroupBy(
            mediaFile => OperatingSystem.IsWindows()
                ? mediaFile.FilePath.ToUpperInvariant()
                : mediaFile.FilePath,
            StringComparer.Ordinal))
        {
            var survivor = group
                .OrderBy(mediaFile => mediaFile.PathKey.StartsWith("legacy:", StringComparison.Ordinal) ? 1 : 0)
                .ThenBy(mediaFile => mediaFile.CreatedAt)
                .ThenBy(mediaFile => mediaFile.Id)
                .First();
            survivor.PathKey = group.Key;
            duplicates.AddRange(group.Where(mediaFile => mediaFile.Id != survivor.Id));
        }

        context.MediaFiles.RemoveRange(duplicates);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ReconcileDuplicateFulfillmentAsync(
            context,
            mediaFiles,
            duplicates,
            contentTypeRegistry,
            fileSystem,
            cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (duplicates.Count > 0)
        {
            logger.LogWarning(
                "Removed {DuplicateCount} duplicate media file associations while normalizing path keys",
                duplicates.Count);
        }
    }

    private static async Task ReconcileDuplicateFulfillmentAsync(
        MagpieDbContext context,
        IReadOnlyCollection<MediaFile> mediaFiles,
        IReadOnlyCollection<MediaFile> duplicates,
        IContentTypeRegistry contentTypeRegistry,
        IFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        var duplicateIds = duplicates.Select(mediaFile => mediaFile.Id).ToHashSet();
        var affectedContentItemIds = duplicates
            .Select(mediaFile => mediaFile.ContentItemId)
            .ToHashSet();
        if (affectedContentItemIds.Count == 0)
        {
            return;
        }

        var contentItems = await context.ContentItems.ToListAsync(cancellationToken).ConfigureAwait(false);
        var hierarchyItems = await context.ContentHierarchyItems
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var contentByHierarchyId = contentItems.ToDictionary(item => item.ContentHierarchyItemId);
        var hierarchyById = hierarchyItems.ToDictionary(item => item.Id);
        var retainedFilesByContentItemId = mediaFiles
            .Where(mediaFile => !duplicateIds.Contains(mediaFile.Id))
            .ToLookup(mediaFile => mediaFile.ContentItemId);
        var now = DateTime.UtcNow;
        var changedHierarchyIds = new HashSet<Guid>();
        foreach (var contentItem in contentItems.Where(item => affectedContentItemIds.Contains(item.Id)))
        {
            if (contentItem.Fulfillment == FulfillmentStatus.FulfilledExternally)
            {
                continue;
            }

            var hierarchyItem = hierarchyById.GetValueOrDefault(contentItem.ContentHierarchyItemId);
            var level = hierarchyItem is null
                ? null
                : contentTypeRegistry.GetDefinitionFor(hierarchyItem.ContentType)?.LevelDefinitions
                    .FirstOrDefault(candidate => candidate.Slug == hierarchyItem.Level);
            var acceptedFileTypes = level?.AcceptedFileTypes ?? [];
            var acceptedSidecarTypes = level?.AcceptedSidecarTypes ?? [];
            var hasPrimary = false;
            foreach (var mediaFile in retainedFilesByContentItemId[contentItem.Id])
            {
                var matchesPrimary = acceptedFileTypes.Count > 0 && await fileSystem
                    .MatchesAnyFileTypeAsync(mediaFile.FilePath, acceptedFileTypes, cancellationToken)
                    .ConfigureAwait(false);
                if (matchesPrimary || acceptedFileTypes.Count == 0 && !await fileSystem
                        .MatchesAnyFileTypeAsync(mediaFile.FilePath, acceptedSidecarTypes, cancellationToken)
                        .ConfigureAwait(false))
                {
                    hasPrimary = true;
                    break;
                }
            }

            var fulfillment = hasPrimary
                ? contentItem.QualityStatus switch
                {
                    QualityStatus.UpgradeWanted => FulfillmentStatus.Upgradable,
                    QualityStatus.Met => FulfillmentStatus.Fulfilled,
                    _ when contentItem.Fulfillment == FulfillmentStatus.Missing => FulfillmentStatus.Fulfilled,
                    _ => contentItem.Fulfillment
                }
                : FulfillmentStatus.Missing;
            if (fulfillment == contentItem.Fulfillment)
            {
                continue;
            }

            contentItem.Fulfillment = fulfillment;
            contentItem.ModifiedAt = now;
            changedHierarchyIds.Add(contentItem.ContentHierarchyItemId);
        }

        var ancestorIds = new HashSet<Guid>();
        foreach (var hierarchyItemId in changedHierarchyIds)
        {
            var parentId = hierarchyById.GetValueOrDefault(hierarchyItemId)?.ParentId;
            while (parentId is { } id && ancestorIds.Add(id))
            {
                parentId = hierarchyById.GetValueOrDefault(id)?.ParentId;
            }
        }

        var statuses = contentByHierarchyId.ToDictionary(entry => entry.Key, entry => entry.Value.Fulfillment);
        foreach (var ancestorId in ancestorIds.OrderByDescending(id => GetHierarchyDepth(id, hierarchyById)))
        {
            var childStatuses = hierarchyItems
                .Where(item => item.ParentId == ancestorId)
                .Select(item => statuses.GetValueOrDefault(item.Id, FulfillmentStatus.Missing))
                .ToArray();
            var fulfillment = AggregateFulfillment(childStatuses);
            statuses[ancestorId] = fulfillment;
            if (contentByHierarchyId.TryGetValue(ancestorId, out var contentItem))
            {
                if (contentItem.Fulfillment != fulfillment)
                {
                    contentItem.Fulfillment = fulfillment;
                    contentItem.ModifiedAt = now;
                }
            }
            else if (fulfillment != FulfillmentStatus.Missing)
            {
                context.ContentItems.Add(new ContentItem
                {
                    Id = Guid.NewGuid(),
                    ContentHierarchyItemId = ancestorId,
                    Fulfillment = fulfillment,
                    CreatedAt = now
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static int GetHierarchyDepth(
        Guid hierarchyItemId,
        IReadOnlyDictionary<Guid, ContentHierarchyItem> hierarchyById)
    {
        var depth = 0;
        var parentId = hierarchyById.GetValueOrDefault(hierarchyItemId)?.ParentId;
        while (parentId is { } id)
        {
            depth++;
            parentId = hierarchyById.GetValueOrDefault(id)?.ParentId;
        }

        return depth;
    }

    private static FulfillmentStatus AggregateFulfillment(IReadOnlyCollection<FulfillmentStatus> statuses)
    {
        if (statuses.Count == 0 ||
            statuses.All(status => status is not FulfillmentStatus.Missing and not FulfillmentStatus.Partial))
        {
            return statuses.Contains(FulfillmentStatus.Upgradable)
                ? FulfillmentStatus.Upgradable
                : FulfillmentStatus.Fulfilled;
        }

        return statuses.Any(status => status != FulfillmentStatus.Missing)
            ? FulfillmentStatus.Partial
            : FulfillmentStatus.Missing;
    }

    private static async Task PrepareDatabaseAsync(
        IHostApplicationBuilder builder,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var services = builder.Services.BuildServiceProvider();
        await using (services.ConfigureAwait(false))
        {
            var scope = services.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var databaseProvider = scope.ServiceProvider.GetService<IDatabaseManagementProvider>();
                if (databaseProvider is IRecoverableDatabaseManagementProvider recoverable)
                {
                    var applied = await recoverable.ApplyPendingRestoreAsync(cancellationToken).ConfigureAwait(false);
                    if (applied)
                    {
                        logger.LogInformation("The host applied the pending database restore before the migration");
                    }
                }

                if (databaseProvider is IStartupValidationDatabaseManagementProvider startupValidating)
                {
                    var result = await startupValidating.ValidateStartupAsync(cancellationToken).ConfigureAwait(false);
                    if (!result.IsValid)
                    {
                        throw new MagpieException(result.ValidationError ?? "The database failed startup validation.");
                    }
                }
            }
        }
    }

    internal static WebApplication EnsureMagpieClusterBackplane(this WebApplication app)
    {
        if (app.Services.GetRequiredService<MagpieHostEnvironment>().IsClustered &&
            app.Services.GetService<IClusterBackplane>() is null)
        {
            throw new MagpieException("Cluster mode requires a cross-node backplane, but none is configured.");
        }

        return app;
    }

    internal static async Task<IReadOnlyList<IExtension>> AddMagpieExtensionsAsync(
        this IHostApplicationBuilder builder,
        CancellationToken cancellationToken = default)
    {
        using var loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
            ConfigureBootstrapConsoleLogging(builder, logging);
        });

        var logger = loggerFactory.CreateLogger(nameof(MagpieHostBuilderExtensions));
        var register = false;
        var extensions = await ReadInstalledExtensionsAsync(builder, cancellationToken).ConfigureAwait(false);

        if (extensions is { Count: 0 })
        {
            extensions = await DiscoverExtensionsAsync(builder, logger, cancellationToken).ConfigureAwait(false);
            register = true;
        }

        var loaded = builder.Services.AddMagpieExtensions(extensions, BuiltInExtensions, loggerFactory);

        if (register)
        {
            await RegisterLoadedExtensionsAsync(
                builder,
                extensions,
                loaded,
                loggerFactory.CreateLogger(nameof(MagpieHostBuilderExtensions)),
                cancellationToken).ConfigureAwait(false);
        }

        builder.Services.AddDefaultProviders();
        builder.AddMagpieClusterBackplane();
        await BackfillMediaFilePathKeysAsync(builder, logger, cancellationToken).ConfigureAwait(false);

        return loaded;
    }

    internal static async Task DispatchLifecycleEventsAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        using var scope = app.Services.CreateScope();
        var activationGenerationRepository = scope.ServiceProvider
            .GetRequiredService<IExtensionActivationGenerationRepository>();
        var generation = await activationGenerationRepository.GetCurrentAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await app.Services.GetRequiredService<IExtensionLifecycleManager>()
                .DispatchStartupHooksAsync(cancellationToken).ConfigureAwait(false);
            await RecordExtensionNodeStatusAsync(
                scope.ServiceProvider,
                generation,
                ExtensionNodeHealth.CurrentHealthy,
                "Extension activation succeeded.",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordExtensionNodeStatusAsync(
                scope.ServiceProvider,
                generation,
                ExtensionNodeHealth.ActivationFailed,
                ex.Message,
                cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static void AddMagpieClusterBackplane(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration[DatabaseConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        switch (builder.Configuration[DatabaseProviderKey]?.ToLowerInvariant())
        {
            case "postgresql" or "postgres":
                builder.Services.AddMagpieDataPostgreSqlClusterBackplane(connectionString);
                break;
            case "sqlserver" or "mssql":
                builder.Services.AddMagpieDataSqlServerClusterBackplane(connectionString);
                break;
        }
    }

    private static async Task RecordExtensionNodeStatusAsync(
        IServiceProvider serviceProvider,
        long appliedGeneration,
        ExtensionNodeHealth health,
        string message,
        CancellationToken cancellationToken)
    {
        var hostEnvironment = serviceProvider.GetRequiredService<MagpieHostEnvironment>();
        var repository = serviceProvider.GetRequiredService<IExtensionNodeStatusRepository>();
        var timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
        await repository.UpsertAsync(
            new ExtensionNodeStatus
            {
                NodeId = hostEnvironment.NodeId,
                AppliedGeneration = appliedGeneration,
                ExtensionHealth = health,
                SchedulerEligible = !hostEnvironment.IsClustered || health is ExtensionNodeHealth.CurrentHealthy,
                LastSeenAt = timeProvider.GetUtcNow().UtcDateTime,
                Message = message,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveDataRoot(string? configuredPath, string contentRoot)
    {
        var dataRoot = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.GetFullPath(Path.Combine(contentRoot, "data"))
            : Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.GetFullPath(Path.Combine(contentRoot, configuredPath));

        Directory.CreateDirectory(dataRoot);

        return dataRoot;
    }

    private static void ConfigureBootstrapConsoleLogging(
        IHostApplicationBuilder builder,
        ILoggingBuilder logging)
    {
        logging.AddConsole(options =>
        {
            if (builder.Properties.ContainsKey(CommandLineLoggingKey))
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            }
        });
    }

    private static IHostApplicationBuilder AddMagpie(IHostApplicationBuilder builder, bool startHostedServices)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        builder.Configuration.AddJsonFile(
            Path.Combine(builder.Environment.ContentRootPath, "config", "appsettings.json"),
            optional: true,
            reloadOnChange: false);
        builder.Configuration.AddEnvironmentVariables();

        var hostOptions = builder.Configuration.GetSection(MagpieHostOptions.SectionName).Get<MagpieHostOptions>()
            ?? new();
        var jobRuntimeOptions = builder.Configuration.GetSection(JobRuntimeOptions.SectionName).Get<JobRuntimeOptions>()
            ?? new();

        builder.Services.AddSingleton(hostOptions);
        builder.Services.AddSingleton(jobRuntimeOptions);

        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        builder.Services.AddCors();

        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<MagpieExceptionHandler>();

        builder.Services.AddMagpieCore(startHostedServices);

        builder.Services.AddMagpieAuth(builder.Configuration);
        builder.Services.AddMagpieHttpResilience();

        AddMagpieObservability(builder, hostOptions);

        var provider = builder.Configuration[DatabaseProviderKey];
        var connectionString = builder.Configuration[DatabaseConnectionStringKey];

        switch (provider?.ToLowerInvariant())
        {
            case null or "sqlite":
                builder.Services.AddMagpieDataSqlite(
                    connectionString ??
                    new SqliteConnectionStringBuilder
                    {
                        DataSource = Path.Combine(
                            ResolveDataRoot(hostOptions.Data.Path, builder.Environment.ContentRootPath),
                            "magpie.db"),
                        Mode = SqliteOpenMode.ReadWriteCreate,
                    }.ToString(),
                    DefaultJobTriggerCatalog.ScheduleTriggers,
                    DefaultJobTriggerCatalog.EventTriggers);
                break;
            case "postgresql" or "postgres":
                {
                    builder.Services.AddMagpieDataPostgreSql(
                        connectionString
                        ?? throw new InvalidOperationException("A PostgreSQL connection string is required."),
                        DefaultJobTriggerCatalog.ScheduleTriggers,
                        DefaultJobTriggerCatalog.EventTriggers);
                    break;
                }
            case "sqlserver" or "mssql":
                {
                    builder.Services.AddMagpieDataSqlServer(
                        connectionString
                        ?? throw new InvalidOperationException("A SQL Server connection string is required."),
                        DefaultJobTriggerCatalog.ScheduleTriggers,
                        DefaultJobTriggerCatalog.EventTriggers);
                    break;
                }
            default:
                throw new InvalidOperationException($"Unknown database provider {provider}");
        }

        return builder;
    }

    private static void AddMagpieObservability(IHostApplicationBuilder builder, MagpieHostOptions hostOptions)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
        {
            return;
        }

        var nodeId = string.IsNullOrWhiteSpace(hostOptions.Cluster.NodeId)
            ? Environment.MachineName
            : hostOptions.Cluster.NodeId;
        var assembly = typeof(MagpieHostBuilderExtensions).Assembly;
        var serviceVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: "magpie", serviceVersion: serviceVersion)
                .AddAttributes(
                [
                    new KeyValuePair<string, object>("magpie.node_id", nodeId),
                    new KeyValuePair<string, object>(
                        "magpie.clustered",
                        hostOptions.Cluster.Mode != ExtensionClusterMode.SingleNode),
                ]))
            .WithTracing(tracing =>
            {
                tracing
                .AddSource("magpie.*")
                .AddAspNetCoreInstrumentation(options =>
                    options.EnrichWithHttpResponse = static (activity, response) =>
                    {
                        var user = response.HttpContext.User;
                        if (user.FindFirst(ClaimTypes.Role)?.Value is { } role)
                        {
                            activity.SetTag("magpie.user.role", role);
                        }

                        if (user.FindFirst(MagpieClaims.AuthProviderType)?.Value is { } scheme)
                        {
                            activity.SetTag("magpie.auth.scheme", scheme);
                        }
                    })
                .AddHttpClientInstrumentation(options =>
                    options.EnrichWithHttpRequestMessage = static (activity, _) =>
                    {
                        if (activity.Parent?.GetTagItem("indexer.id") is string indexerId)
                        {
                            activity.SetTag("magpie.indexer.id", indexerId);
                        }

                        if (activity.Parent?.GetTagItem("magpie.media_server.id") is string mediaServerId)
                        {
                            activity.SetTag("magpie.media_server.id", mediaServerId);
                        }
                    })
                .AddOtlpExporter();
            })
            .WithMetrics(metrics => metrics
                .AddMeter("magpie.*")
                .AddMeter("Polly")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter())
            .WithLogging(
                logging => logging.AddOtlpExporter(),
                options =>
                {
                    options.IncludeFormattedMessage = true;
                    options.IncludeScopes = true;
                });
    }

    private static async Task<IReadOnlyList<Extension>> ReadInstalledExtensionsAsync(
        IHostApplicationBuilder builder,
        CancellationToken cancellationToken)
    {
        var context = CreateBootstrapDbContext(builder.Configuration, builder.Environment.ContentRootPath);

        await using var _ = context.ConfigureAwait(false);

        return await (
            from extension in context.Extensions.AsNoTracking()
            where extension.Installed && extension.Enabled
            orderby extension.LoadOrder, extension.PackageId
            select extension)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<Extension>> DiscoverExtensionsAsync(
        IHostApplicationBuilder builder,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var options = builder.Configuration.GetSection(MagpieHostOptions.SectionName).Get<MagpieHostOptions>()
            ?? new MagpieHostOptions();
        var extensionsRoot = ResolveExtensionsRoot(options.Extensions.Path, builder.Environment.ContentRootPath);
        if (!Directory.Exists(extensionsRoot))
        {
            return [];
        }

        var context = CreateBootstrapDbContext(builder.Configuration, builder.Environment.ContentRootPath);
        await using var contextHandle = context.ConfigureAwait(false);

        if (await context.Extensions.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        var discovered = new List<Extension>();

        foreach (var directory in Directory
            .EnumerateDirectories(extensionsRoot)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetFileName(directory).StartsWith('.'))
            {
                continue;
            }

            var depsPath = Directory.EnumerateFiles(directory, "*.deps.json").FirstOrDefault();
            if (depsPath is null)
            {
                logger.LogWarning("Extension directory {Directory} has no deps.json. The host skipped it", directory);
                continue;
            }

            var assemblyName = Path.GetFileName(depsPath)[..^".deps.json".Length];
            var assemblyPath = Path.Combine(directory, assemblyName + ".dll");
            if (!File.Exists(assemblyPath))
            {
                logger.LogWarning("Extension assembly {AssemblyPath} does not exist, so the host " +
                    "skipped this extension directory and did not load the extension", assemblyPath);
                continue;
            }

            discovered.Add(new Extension
            {
                Id = Guid.NewGuid(),
                PackageId = assemblyName,
                Name = assemblyName,
                Version = AssemblyName.GetAssemblyName(assemblyPath).Version?.ToString(3) ?? "0.0.0",
                FilePath = assemblyPath,
                AssemblyName = assemblyName,
                Installed = true,
                Enabled = true,
                CreatedAt = DateTime.UtcNow,
            });
        }

        return discovered;
    }

    private static async Task RegisterLoadedExtensionsAsync(
        IHostApplicationBuilder builder,
        IReadOnlyList<Extension> extensions,
        IReadOnlyList<IExtension> loaded,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        foreach (var extension in extensions)
        {
            var match = loaded.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.GetType().Assembly.GetName().Name,
                    extension.AssemblyName,
                    StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                continue;
            }

            extension.Name = match.Name;
            extension.Version = match.Version.ToString();
            extension.TargetABI = match.TargetABI;
        }

        var context = CreateBootstrapDbContext(builder.Configuration, builder.Environment.ContentRootPath);
        await using var contextHandle = context.ConfigureAwait(false);

        context.Extensions.AddRange(extensions);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("The host registered {ExtensionCount} bundled extensions on " +
            "first start", extensions.Count);
    }

    private static string ResolveExtensionsRoot(string? configuredPath, string contentRoot)
    {
        return string.IsNullOrWhiteSpace(configuredPath)
            ? Path.GetFullPath(Path.Combine(contentRoot, "extensions"))
            : Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.GetFullPath(Path.Combine(contentRoot, configuredPath));
    }

    private static MagpieDbContext CreateBootstrapDbContext(IConfiguration configuration, string contentRootPath)
    {
        var provider = configuration[DatabaseProviderKey];
        var connectionString = configuration[DatabaseConnectionStringKey];
        var options = configuration.GetSection(MagpieHostOptions.SectionName).Get<MagpieHostOptions>()
            ?? new MagpieHostOptions();

        return provider?.ToLowerInvariant() switch
        {
            "sqlite" or null => SqliteDbContextFactory.Create(
                connectionString ??
                new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(ResolveDataRoot(options.Data.Path, contentRootPath), "magpie.db"),
                    Mode = SqliteOpenMode.ReadWriteCreate,
                }.ToString(),
                DefaultJobTriggerCatalog.ScheduleTriggers,
                DefaultJobTriggerCatalog.EventTriggers),
            "postgresql" or "postgres" => PostgreSqlDbContextFactory.Create(
                connectionString ??
                throw new InvalidOperationException("A PostgreSQL connection string is required."),
                DefaultJobTriggerCatalog.ScheduleTriggers,
                DefaultJobTriggerCatalog.EventTriggers),
            "sqlserver" or "mssql" => SqlServerDbContextFactory.Create(
                connectionString ??
                throw new InvalidOperationException("A SQL Server connection string is required."),
                DefaultJobTriggerCatalog.ScheduleTriggers,
                DefaultJobTriggerCatalog.EventTriggers),
            _ =>
                throw new InvalidOperationException($"Unknown database provider {provider}."),
        };
    }
}
