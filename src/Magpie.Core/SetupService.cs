using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Database;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Models.Patch;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core;

internal sealed class SetupService(
    MagpieHostEnvironment hostEnvironment,
    ISettingsService settingsService,
    IUserService userService,
    IUserRepository users,
    ITransactionManager transactionManager,
    IDatabaseManagementProvider databaseManagement,
    IFileSystem fileSystem,
    ILogger<SetupService> logger) : ISetupService
{
    private static readonly string[] SupportedDatabaseProviders = ["sqlite", "postgresql", "sqlserver"];
    private static readonly Guid SystemUserId = new("11111111-1111-1111-1111-111111111111");
    private const string SetupGroup = "setup";
    private const string CompleteKey = "complete";

    public string DatabaseProvider => databaseManagement.Provider;

    public async Task<bool> IsCompleteAsync(CancellationToken cancellationToken = default)
    {
        var group = await settingsService.GetSystemGroupAsync(SetupGroup, cancellationToken).ConfigureAwait(false);
        return string.Equals(
            group?.Group.Settings.FirstOrDefault(setting => setting.Key == CompleteKey)?.Value,
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> HasLocalAdministratorAsync(CancellationToken cancellationToken = default)
    {
        var admins = await userService.QueryAsync(
            new UserQuery { Role = Role.Admin, Enabled = true, ProviderType = "local" },
            new PagingParameter { PageNumber = 1, PageSize = 1 },
            null,
            cancellationToken).ConfigureAwait(false);
        return admins.TotalItems > 0;
    }

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await databaseManagement.CanConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The database connection failed");
            return false;
        }
    }

    public async Task<IReadOnlyCollection<string>> GetAppliedMigrationsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await databaseManagement.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The database migrations could not be read");
            return [];
        }
    }

    public async Task<DatabaseSetupResult> ConfigureDatabaseAsync(
        string provider,
        string? connectionString,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new MagpieException("Database provider is required.");
        }

        if (await IsCompleteAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new MagpieException("Setup is complete.");
        }

        var providerSlug = provider.Trim().ToLowerInvariant();
        if (!SupportedDatabaseProviders.Contains(providerSlug))
        {
            throw new MagpieException("The database provider is not supported.");
        }

        var normalizedConnectionString = string.IsNullOrWhiteSpace(connectionString) ? null : connectionString.Trim();
        if (providerSlug is "postgresql" or "sqlserver" && normalizedConnectionString is null)
        {
            throw new MagpieException("A connection string is required.");
        }

        var path = Path.Combine(hostEnvironment.ConfigRoot, "appsettings.json");
        var root = await LoadSettingsAsync(path, cancellationToken).ConfigureAwait(false);
        var magpie = root["Magpie"] as JsonObject ?? new JsonObject();
        root["Magpie"] = magpie;
        magpie["Database"] = new JsonObject
        {
            ["Provider"] = providerSlug,
            ["ConnectionString"] = normalizedConnectionString
        };
        await WriteSettingsAsync(path, root, cancellationToken).ConfigureAwait(false);

        return new() { Provider = providerSlug, RestartRequired = true };
    }

    public async Task<User> CreateAdministratorAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (await IsCompleteAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new MagpieException("Setup is complete.");
        }

        return await transactionManager.ExecuteWithTransactionAsync(
            async token =>
            {
                await users.LockAuthorizationAsync(SystemUserId, token).ConfigureAwait(false);

                if (await HasLocalAdministratorAsync(token).ConfigureAwait(false))
                {
                    throw new MagpieException("The administrator account is already claimed.");
                }

                if (string.IsNullOrWhiteSpace(username) || username.Trim().Length < 3)
                {
                    throw new MagpieException("The username must have at least 3 characters.");
                }

                var normalizedUsername = username.Trim();
                if (await userService.GetAuthenticationUserAsync(normalizedUsername, token).ConfigureAwait(false)
                    is not null)
                {
                    throw new MagpieException("The username is unavailable.");
                }

                return await userService.CreateAsync(
                    new() { Username = normalizedUsername, Password = password, Role = Role.Admin },
                    SystemUserId,
                    token).ConfigureAwait(false) ?? throw new MagpieException("The username is unavailable.");
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (!await HasLocalAdministratorAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new MagpieException("An administrator account must be created before setup can be marked complete.");
        }

        var group = await settingsService.GetSystemGroupAsync(SetupGroup, cancellationToken).ConfigureAwait(false);
        var settings = group?.Group.Settings.Any(setting => setting.Key == CompleteKey) is true
            ? new DictionaryPatch<string, string, string>
            {
                Update = new() { [CompleteKey] = "true" }
            }
            : new DictionaryPatch<string, string, string>
            {
                Add = new() { [CompleteKey] = "true" }
            };
        await settingsService.PatchSystemGroupAsync(
            SetupGroup,
            new SettingsGroupPatch { Name = SetupGroup, Settings = settings },
            SystemUserId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonObject> LoadSettingsAsync(string path, CancellationToken cancellationToken)
    {
        if (!fileSystem.FileExists(path))
        {
            return [];
        }

        var stream = fileSystem.OpenRead(path);
        await using var _ = stream.ConfigureAwait(false);
        return await JsonNode.ParseAsync(
            stream,
            documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            },
            cancellationToken: cancellationToken).ConfigureAwait(false) as JsonObject ?? [];
    }

    private async Task WriteSettingsAsync(string path, JsonObject root, CancellationToken cancellationToken)
    {
        fileSystem.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        var stream = fileSystem.CreateFile(temporaryPath);
        await using (stream.ConfigureAwait(false))
        {
            var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            await using (writer.ConfigureAwait(false))
            {
                root.WriteTo(writer);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        fileSystem.Move(temporaryPath, path, overwrite: true);
    }
}
