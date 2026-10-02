using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Jellyfin.Models;
using Magpie.Models;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Jellyfin;

internal sealed class JellyfinLibraryService(
    IMediaServerService mediaServerService,
    IJellyfinClient client,
    ILogger<JellyfinLibraryService> logger)
{
    public async Task<JellyfinLibrariesResult> ListLibrariesAsync(
        Guid? mediaServerId,
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken)
    {
        var overlay = mediaServerId.HasValue && settings is not null
            ? settings.Where(pair =>
                    !string.Equals(pair.Key, JellyfinSettings.HostKey, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value)
            : settings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var merged = await mediaServerService
            .ResolveSettingsAsync(
                mediaServerId,
                overlay,
                cancellationToken)
            .ConfigureAwait(false);

        if (!merged.TryGetValueIgnoringCase(JellyfinSettings.HostKey, out var rawHost) ||
            string.IsNullOrWhiteSpace(rawHost) ||
            !merged.TryGetValueIgnoringCase(JellyfinSettings.ApiKeyKey, out var apiKey) ||
            string.IsNullOrEmpty(apiKey))
        {
            return new JellyfinLibrariesResult();
        }

        JellyfinSettings parsed;
        try
        {
            parsed = merged.ToJellyfinSettings();
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "The Jellyfin library listing received invalid settings for {Host}.", rawHost);
            return new JellyfinLibrariesResult { FailureKind = ConfigurationFailureKind.Fatal };
        }

        IReadOnlyList<VirtualFolder> folders;
        try
        {
            folders = await client.GetVirtualFoldersAsync(parsed, cancellationToken).ConfigureAwait(false);
        }
        catch (JellyfinAuthenticationException ex)
        {
            logger.LogWarning(ex, "Jellyfin at {Host} refused the API key for the library listing.", parsed.Host);
            return new JellyfinLibrariesResult { FailureKind = ConfigurationFailureKind.Auth };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The Jellyfin library listing failed for {Host}.", parsed.Host);
            return new JellyfinLibrariesResult { FailureKind = ConfigurationFailureKind.Transient };
        }

        var options = new List<SettingSchemaOption>(folders.Count);
        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder.ItemId))
            {
                continue;
            }

            options.Add(new SettingSchemaOption
            {
                Value = folder.ItemId,
                Label = string.IsNullOrWhiteSpace(folder.Name) ? folder.ItemId : folder.Name,
                Description = folder.CollectionType
            });
        }

        return new JellyfinLibrariesResult { Options = options };
    }
}
