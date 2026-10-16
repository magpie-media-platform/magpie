using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using Magpie.Abstractions;
using Magpie.Abstractions.Database;
using Magpie.Abstractions.Security;
using Magpie.Api;
using Magpie.App;
using Magpie.App.Hosting;
using Magpie.Core.Config;
using Magpie.Core.Extensions;
using Magpie.Core.Jobs;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Http;

using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        if (hostStatus.Mode == HostMode.WindowsService && OperatingSystem.IsWindows())
        {
            builder.Services.AddWindowsService();

            builder.Services.RemoveAll<IHostLifetime>();
            builder.Services.AddSingleton<IHostLifetime, RestartAwareWindowsServiceLifetime>();
        }
        else if (hostStatus.Mode == HostMode.Systemd)
        {
            builder.Services.AddSystemd();
        }

        var options = builder.Configuration.GetSection(MagpieHostOptions.SectionName).Get<MagpieHostOptions>()
            ?? new();

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

    internal static async Task PrepareMagpieDatabaseAsync(
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
    }

    private static bool Serves(MagpieHostOptions options, NodeRole role) =>
        options.Cluster.Roles.Count == 0 || options.Cluster.Roles.Contains(role);

    internal static ForwardedHeadersOptions BuildForwardedHeadersOptions(IReadOnlyList<string> knownProxies)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        foreach (var proxy in knownProxies)
        {
            if (System.Net.IPNetwork.TryParse(proxy, out var network))
            {
                options.KnownIPNetworks.Add(network);
            }
            else if (IPAddress.TryParse(proxy, out var address))
            {
                options.KnownProxies.Add(address);
            }
            else
            {
                throw new InvalidOperationException($"Known proxy {proxy} is not an address or a CIDR range.");
            }
        }

        return options;
    }

    internal static bool IsNewInstance(bool hasSchema, long activationGeneration, bool hasExtensions)
    {
        return !hasSchema || (activationGeneration == 0 && !hasExtensions);
    }

    internal static WebApplication UseMagpieRequestPipeline(this WebApplication app)
    {
        var hostStatus = app.Services.GetRequiredService<HostStatusInfo>();
        var backgroundServices = app.Services.GetServices<IHostedService>().OfType<BackgroundService>().ToArray();
        app.Lifetime.ApplicationStopping.Register(() =>
        {
            if (backgroundServices.Any(service => service.ExecuteTask is { IsFaulted: true } or { IsCanceled: true }))
            {
                hostStatus.ShutdownExitCode ??= MagpieExitCodes.StartupFailed;
            }
        });

        var hostOptions = app.Services.GetRequiredService<MagpieHostOptions>();
        if (hostOptions.KnownProxies.Count > 0)
        {
            app.UseForwardedHeaders(BuildForwardedHeadersOptions(hostOptions.KnownProxies));
        }

        app.UseExceptionHandler();
        var allowedOrigins = hostOptions.AllowedOrigins;
        if (allowedOrigins.Count > 0)
        {
            app.UseCors(policy => policy
                .WithOrigins([.. allowedOrigins])
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
        }

        app.UseMagpieDatabaseMigrationGate();
        if (Serves(hostOptions, NodeRole.Ui))
        {
            app.UseStaticFiles();
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMagpieSetupRedirect();
        if (Serves(hostOptions, NodeRole.Api))
        {
            app.MapMagpieEndpoints();
        }
        else
        {
            app.MapMagpieHealthEndpoints();
        }

        if (Serves(hostOptions, NodeRole.Ui))
        {
            app.MapGet(WebManifest.RequestPath, WebManifest.GetAsync).AllowAnonymous();
            app.MapFallbackToFile("index.html").AllowAnonymous();
        }

        return app;
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
                        logger.LogInformation("Applied pending database restore before migration");
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

    internal static async Task AddMagpieExtensionsAsync(
        this IHostApplicationBuilder builder,
        CancellationToken cancellationToken = default)
    {
        using var loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
            ConfigureBootstrapConsoleLogging(builder, logging);
        });

        var logger = loggerFactory.CreateLogger(nameof(MagpieHostBuilderExtensions));
        var options = builder.Configuration.GetSection(MagpieHostOptions.SectionName).Get<MagpieHostOptions>()
            ?? new();
        var extensionsRoot = ResolveExtensionsRoot(options.Extensions.Path, builder.Environment.ContentRootPath);
        var context = CreateBootstrapDbContext(builder.Configuration, builder.Environment.ContentRootPath);
        await using var contextHandle = context.ConfigureAwait(false);

        var hasSchema = await HasSchemaAsync(context, cancellationToken).ConfigureAwait(false);

        var activationGeneration = hasSchema
            ? await ReadActivationGenerationAsync(context, cancellationToken).ConfigureAwait(false)
            : 0;

        var newInstance = IsNewInstance(
            hasSchema,
            activationGeneration,
            hasSchema && await context.Extensions.AnyAsync(cancellationToken).ConfigureAwait(false));
        var extensions = newInstance
            ? DiscoverExtensions(extensionsRoot, logger)
            : await ReadInstalledExtensionsAsync(context, cancellationToken).ConfigureAwait(false);
        var packageVersions = new Dictionary<Guid, string>();
        foreach (var extension in extensions)
        {
            var packageVersion = newInstance
                ? extension.Version
                : ReadPackageVersion(extensionsRoot, extension.FilePath, logger);
            if (packageVersion is not null)
            {
                packageVersions[extension.Id] = packageVersion;
            }
        }

        var loaded = builder.Services.AddMagpieExtensions(extensions, BuiltInExtensions, loggerFactory);

        builder.Services.AddDefaultProviders();
        builder.AddMagpieClusterBackplane();
        builder.Services.AddSingleton(new LoadedExtensionSet(
            newInstance ? extensions : [],
            loaded,
            packageVersions,
            activationGeneration));
        builder.Services.AddSingleton<DatabaseStartupService>();
    }

    private static void AddMagpieClusterBackplane(this IHostApplicationBuilder builder)
    {
        var (provider, connectionString) = ResolveDatabaseConfiguration(
            builder.Configuration,
            builder.Environment.ContentRootPath);
        switch (provider)
        {
            case "postgresql":
                builder.Services.AddMagpieDataPostgreSqlClusterBackplane(connectionString);
                break;
            case "sqlserver":
                builder.Services.AddMagpieDataSqlServerClusterBackplane(connectionString);
                break;
        }
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

    private static void ConfigureBootstrapConsoleLogging(IHostApplicationBuilder builder, ILoggingBuilder logging)
    {
        logging.AddConsole(options =>
        {
            if (builder.Properties.ContainsKey(CommandLineLoggingKey))
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            }
        });
    }

    internal static TimeSpan ResolveMagpieShutdownTimeout(this WebApplicationBuilder builder)
    {
        AddMagpieConfiguration(builder);
        using var app = builder.Build();

        return app.Services.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout;
    }

    private static void AddMagpieConfiguration(IHostApplicationBuilder builder)
    {
        builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        builder.Configuration.AddJsonFile(
            Path.Combine(builder.Environment.ContentRootPath, "config", "appsettings.json"),
            optional: true,
            reloadOnChange: false);
        builder.Configuration.AddEnvironmentVariables();
    }

    private static IHostApplicationBuilder AddMagpie(IHostApplicationBuilder builder, bool startHostedServices)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        AddMagpieConfiguration(builder);

        var hostOptions = builder.Configuration.GetSection(MagpieHostOptions.SectionName).Get<MagpieHostOptions>()
            ?? new();
        var jobRuntimeOptions = builder.Configuration.GetSection(JobRuntimeOptions.SectionName).Get<JobRuntimeOptions>()
            ?? new();
        var searchOptions = builder.Configuration.GetSection(SearchOptions.SectionName).Get<SearchOptions>()
            ?? new();
        var httpOptions = builder.Configuration.GetSection(HttpOptions.SectionName).Get<HttpOptions>()
            ?? new();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(httpOptions.Timeout, TimeSpan.Zero);

        builder.Services.AddSingleton(hostOptions);
        builder.Services.AddSingleton(jobRuntimeOptions);
        builder.Services.AddSingleton(searchOptions);
        builder.Services.AddSingleton(httpOptions);
        builder.Services.ConfigureHttpClientDefaults(http =>
            http.ConfigureHttpClient(client => client.Timeout = httpOptions.Timeout));

        builder.Services.ConfigureHttpJsonOptions(o => ConfigureApiJsonOptions(o.SerializerOptions));
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        builder.Services.AddCors();

        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<MagpieExceptionHandler>();

        builder.Services.AddMagpieCore(startHostedServices, Serves(hostOptions, NodeRole.JobRunner));

        if (startHostedServices)
        {
            builder.Services.AddHostedService<DatabaseStartupBackgroundService>();
        }

        builder.Services.AddMagpieAuth(builder.Configuration);

        AddMagpieObservability(builder, hostOptions);

        var (provider, connectionString) = ResolveDatabaseConfiguration(
            builder.Configuration,
            builder.Environment.ContentRootPath);
        switch (provider)
        {
            case "sqlite":
                builder.Services.AddMagpieDataSqlite(
                    connectionString,
                    DefaultJobTriggerCatalog.ScheduleTriggers,
                    DefaultJobTriggerCatalog.EventTriggers);
                break;
            case "postgresql":
                builder.Services.AddMagpieDataPostgreSql(
                    connectionString,
                    DefaultJobTriggerCatalog.ScheduleTriggers,
                    DefaultJobTriggerCatalog.EventTriggers,
                    hostOptions.Data.PostgreSqlToolsDirectory);
                break;
            case "sqlserver":
                builder.Services.AddMagpieDataSqlServer(
                    connectionString,
                    DefaultJobTriggerCatalog.ScheduleTriggers,
                    DefaultJobTriggerCatalog.EventTriggers);
                break;
        }

        return builder;
    }

    internal static void ConfigureApiJsonOptions(JsonSerializerOptions options)
    {
        options.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.Converters.Add(new JsonStringEnumConverter());
    }

    internal static void AddMagpieObservability(IHostApplicationBuilder builder, MagpieHostOptions hostOptions)
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
                    new("magpie.node_id", nodeId),
                    new("magpie.clustered", hostOptions.Cluster.Mode != ExtensionClusterMode.SingleNode),
                ]))
            .WithTracing(tracing =>
            {
                tracing
                .AddSource("magpie.*")
                .AddAspNetCoreInstrumentation(options =>
                {
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
                    };
                })
                .AddHttpClientInstrumentation(options =>
                {
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
                    };
                })
                .AddEntityFrameworkCoreInstrumentation()
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
                    options.IncludeScopes = false;
                });
    }

    private static async Task<IReadOnlyList<Extension>> ReadInstalledExtensionsAsync(
        MagpieDbContext context,
        CancellationToken cancellationToken)
    {
        return await (
            from extension in context.Extensions.AsNoTracking()
            where extension.Installed && extension.Enabled
            orderby extension.LoadOrder, extension.PackageId
            select new Extension
            {
                Id = extension.Id,
                PackageId = extension.PackageId,
                Name = extension.Name,
                Version = extension.Version,
                FilePath = extension.FilePath,
                AssemblyName = extension.AssemblyName,
                Installed = extension.Installed,
                Enabled = extension.Enabled,
                LoadOrder = extension.LoadOrder
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<long> ReadActivationGenerationAsync(
        MagpieDbContext context,
        CancellationToken cancellationToken)
    {
        return await context.ExtensionActivationStates.AsNoTracking()
            .Select(state => state.Generation)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static List<Extension> DiscoverExtensions(string extensionsRoot, ILogger logger)
    {
        var discovered = new List<Extension>();
        if (!Directory.Exists(extensionsRoot))
        {
            return discovered;
        }

        foreach (var directory in Directory
            .EnumerateDirectories(extensionsRoot)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetFileName(directory).StartsWith('.'))
            {
                continue;
            }

            var nuspec = ReadNuspec(directory, logger);
            var packageId = nuspec?.Id;
            if (packageId is null || nuspec?.Version is not { } version)
            {
                logger.LogWarning("Extension directory {Directory} has no valid nuspec, skipped", directory);
                continue;
            }

            var assemblyPath = Directory
                .EnumerateFiles(directory, packageId + ".dll", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (assemblyPath is null)
            {
                logger.LogWarning(
                    "Extension directory {Directory} has no {AssemblyName}.dll, skipped",
                    directory,
                    packageId);
                continue;
            }

            discovered.Add(new Extension
            {
                Id = Guid.NewGuid(),
                PackageId = packageId,
                Name = nuspec.Title ?? packageId,
                Author = nuspec.Authors,
                Description = nuspec.Description,
                Version = version,
                FilePath = assemblyPath,
                AssemblyName = packageId,
                Installed = true,
                Enabled = true,
                Managed = true,
                NuGetId = packageId,
                CreatedAt = DateTime.UtcNow,
            });
        }

        return discovered;
    }

    private static string? ReadPackageVersion(string extensionsRoot, string filePath, ILogger logger)
    {
        var relativePath = Path.GetRelativePath(extensionsRoot, filePath);
        var packageDirectory = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return Path.IsPathRooted(relativePath) || packageDirectory == ".."
            ? null
            : ReadNuspec(Path.Combine(extensionsRoot, packageDirectory), logger)?.Version;
    }

    private static NuspecMetadata? ReadNuspec(string directory, ILogger logger)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            var nuspecPath = Directory.EnumerateFiles(directory, "*.nuspec").FirstOrDefault();
            if (nuspecPath is null)
            {
                return null;
            }

            var metadata = XDocument.Load(nuspecPath).Root?.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "metadata");
            return new(
                ReadNuspecValue(metadata, "id"),
                ReadNuspecValue(metadata, "version"),
                ReadNuspecValue(metadata, "title"),
                ReadNuspecValue(metadata, "authors"),
                ReadNuspecValue(metadata, "description"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The host failed to read the extension nuspec in {Directory}", directory);
            return null;
        }
    }

    private static string? ReadNuspecValue(XElement? metadata, string name)
    {
        var value = metadata?.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string ResolveExtensionsRoot(string? configuredPath, string contentRoot)
    {
        return string.IsNullOrWhiteSpace(configuredPath)
            ? Path.GetFullPath(Path.Combine(contentRoot, "extensions"))
            : Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.GetFullPath(Path.Combine(contentRoot, configuredPath));
    }

    private static async Task<bool> HasSchemaAsync(MagpieDbContext context, CancellationToken cancellationToken)
    {
        var databaseCreator = context.GetService<IRelationalDatabaseCreator>();
        return await databaseCreator.ExistsAsync(cancellationToken).ConfigureAwait(false) &&
            (await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).Any();
    }

    internal static (string Provider, string ConnectionString) ResolveDatabaseConfiguration(
        IConfiguration configuration,
        string contentRootPath)
    {
        var configuredProvider = configuration[DatabaseProviderKey];
        var provider = configuredProvider?.ToLowerInvariant() switch
        {
            null or "sqlite" => "sqlite",
            "postgresql" or "postgres" => "postgresql",
            "sqlserver" or "mssql" => "sqlserver",
            _ => throw new InvalidOperationException($"Unknown database provider {configuredProvider}.")
        };
        var connectionString = configuration[DatabaseConnectionStringKey];
        if (connectionString is null && provider == "sqlite")
        {
            var options = configuration.GetSection(MagpieHostOptions.SectionName).Get<MagpieHostOptions>()
                ?? new();
            connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(ResolveDataRoot(options.Data.Path, contentRootPath), "magpie.db"),
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"A {provider} connection string is required.");
        }

        return (provider, connectionString);
    }

    private static MagpieDbContext CreateBootstrapDbContext(IConfiguration configuration, string contentRootPath)
    {
        var (provider, connectionString) = ResolveDatabaseConfiguration(configuration, contentRootPath);
        return provider switch
        {
            "sqlite" => SqliteDbContextFactory.Create(
                connectionString,
                DefaultJobTriggerCatalog.ScheduleTriggers,
                DefaultJobTriggerCatalog.EventTriggers),
            "postgresql" => PostgreSqlDbContextFactory.Create(
                connectionString,
                DefaultJobTriggerCatalog.ScheduleTriggers,
                DefaultJobTriggerCatalog.EventTriggers),
            "sqlserver" => SqlServerDbContextFactory.Create(
                connectionString,
                DefaultJobTriggerCatalog.ScheduleTriggers,
                DefaultJobTriggerCatalog.EventTriggers),
            _ => throw new InvalidOperationException($"Unknown database provider {provider}.")
        };
    }

    private sealed record NuspecMetadata(
        string? Id,
        string? Version,
        string? Title,
        string? Authors,
        string? Description);
}
