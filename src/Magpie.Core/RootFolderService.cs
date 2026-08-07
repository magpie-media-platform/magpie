using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Storage;
using Magpie.Sdk.Services;

namespace Magpie.Core;

internal sealed class RootFolderService(
    IRootFolderRepository rootFolderRepository,
    IFileSystem fileSystem,
    IContentTypeRegistry contentTypeRegistry,
    IQualityService qualityService) : IRootFolderService
{
    private static DiskSpaceResult BuildDiskSpaceResult(
        IEnumerable<RootFolder> rootFolders,
        IFileSystem fileSystem)
    {
        var diskSpaceInfos = new List<DiskSpaceInfo>();
        var volumes = new List<(string Name, long TotalBytes, long FreeBytes)>();

        foreach (var folder in rootFolders)
        {
            var driveInfo = GetDriveInfoForPath(folder.Path, fileSystem);
            if (driveInfo is null)
            {
                diskSpaceInfos.Add(CreateUnavailableDiskSpaceInfo(folder));
                continue;
            }

            diskSpaceInfos.Add(new DiskSpaceInfo
            {
                Id = folder.Id,
                Name = folder.Name,
                Path = folder.Path,
                TotalBytes = driveInfo.TotalSize,
                FreeBytes = driveInfo.AvailableFreeSpace,
                UsedBytes = driveInfo.TotalSize - driveInfo.AvailableFreeSpace
            });

            if (!volumes.Any(volume => fileSystem.PathsEqual(volume.Name, driveInfo.Name)))
            {
                volumes.Add((driveInfo.Name, driveInfo.TotalSize, driveInfo.AvailableFreeSpace));
            }
        }

        var totalBytes = volumes.Sum(volume => volume.TotalBytes);
        var freeBytes = volumes.Sum(volume => volume.FreeBytes);

        return new DiskSpaceResult
        {
            Folders = [.. diskSpaceInfos],
            TotalBytes = totalBytes,
            FreeBytes = freeBytes,
            UsedBytes = totalBytes - freeBytes
        };
    }

    private static DiskSpaceInfo CreateUnavailableDiskSpaceInfo(RootFolder folder)
    {
        return new DiskSpaceInfo
        {
            Id = folder.Id,
            Name = folder.Name,
            Path = folder.Path,
            TotalBytes = 0,
            FreeBytes = 0,
            UsedBytes = 0
        };
    }

    private static DriveInfo? GetDriveInfoForPath(string path, IFileSystem fileSystem)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fullPath = fileSystem.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);

            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            return DriveInfo.GetDrives()
                .FirstOrDefault(d => d.IsReady &&
                    fileSystem.PathsEqual(d.Name, root));
        }
        catch
        {
            return null;
        }
    }

    private void ValidateContentType(string contentType, string parameterName)
    {
        if (contentTypeRegistry.Exists(contentType))
        {
            return;
        }

        var registeredContentTypes = string.Join(
            ", ",
            contentTypeRegistry.Definitions
                .Select(definition => definition.Slug)
                .OrderBy(slug => slug, StringComparer.Ordinal));
        throw new ArgumentException(
            $"Content type must be one of: {registeredContentTypes}.",
            parameterName);
    }

    private async Task<Dictionary<string, RootFolderDefault>> ValidateDefaultsAsync(
        string contentType,
        IReadOnlyDictionary<string, RootFolderDefault> requested,
        CancellationToken cancellationToken)
    {
        var definition = contentTypeRegistry.GetDefinitionFor(contentType)
            ?? throw new ArgumentException($"Content type {contentType} is not registered.", nameof(contentType));
        var levelsBySlug = definition.LevelDefinitions.ToDictionary(level => level.Slug, StringComparer.Ordinal);
        var tierLists = (await qualityService.ListTierListsAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(tierList => tierList.Id);
        var validated = new Dictionary<string, RootFolderDefault>(StringComparer.Ordinal);
        foreach (var value in requested)
        {
            if (string.IsNullOrWhiteSpace(value.Key) || !levelsBySlug.ContainsKey(value.Key))
            {
                throw new ArgumentException(
                    $"Level {value.Key} is not defined for content type {contentType}.",
                    nameof(requested));
            }

            if (value.Value.QualityTierListId is not { } qualityTierListId ||
                !tierLists.TryGetValue(qualityTierListId, out var tierList))
            {
                throw new ArgumentException(
                    $"Quality tier list {value.Value.QualityTierListId} does not exist.",
                    nameof(requested));
            }

            validated.Add(value.Key, new RootFolderDefault { QualityTierListId = qualityTierListId });
        }

        foreach (var rootLevel in definition.LevelDefinitions.Where(level => level.ParentSlug is null))
        {
            if (!validated.ContainsKey(rootLevel.Slug))
            {
                throw new ArgumentException(
                    $"Root level {rootLevel.DisplayName} ({rootLevel.Slug}) requires a default quality tier list.",
                    nameof(requested));
            }
        }

        return validated;
    }

    public async Task<IReadOnlyList<RootFolder>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rootFolders = await rootFolderRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return [.. rootFolders];
    }

    public async Task<RootFolder?> GetAsync(Guid rootFolderId, CancellationToken cancellationToken = default)
    {
        return await rootFolderRepository.GetByIdAsync(rootFolderId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RootFolder> CreateAsync(
        RootFolderCreate create,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);

        if (string.IsNullOrWhiteSpace(create.Name))
        {
            throw new ArgumentException("Name is required.", nameof(create.Name));
        }

        if (string.IsNullOrWhiteSpace(create.Path))
        {
            throw new ArgumentException("Path is required.", nameof(create.Path));
        }

        if (!Path.IsPathFullyQualified(create.Path))
        {
            throw new ArgumentException("Path must be absolute.", nameof(create.Path));
        }

        var fullPath = Path.TrimEndingDirectorySeparator(fileSystem.GetFullPath(create.Path));
        if (!fileSystem.DirectoryExists(fullPath))
        {
            throw new ArgumentException("Directory does not exist.", nameof(create.Path));
        }

        var rootFolders = await rootFolderRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        if (rootFolders.Any(rootFolder => fileSystem.PathsEqual(rootFolder.Path, fullPath)))
        {
            throw new ArgumentException("Path is already configured as a root folder.", nameof(create.Path));
        }

        ValidateContentType(create.ContentType, nameof(create.ContentType));

        var defaults = await ValidateDefaultsAsync(
            create.ContentType,
            create.Defaults,
            cancellationToken).ConfigureAwait(false);

        var model = new RootFolder
        {
            Name = create.Name,
            Path = fullPath,
            ContentType = create.ContentType,
            IsDefault = create.IsDefault,
            DefaultMonitoring = create.DefaultMonitoring,
            Defaults = defaults
        };

        if (create.RenameTemplate is not null)
        {
            model.RenameTemplate = create.RenameTemplate;
        }

        if (create.UpgradeFileHandling is { } upgradeFileHandling)
        {
            model.UpgradeFileHandling = upgradeFileHandling;
        }

        return await rootFolderRepository.CreateAsync(model, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> PatchAsync(
        Guid rootFolderId,
        RootFolderPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var existing = await rootFolderRepository.GetByIdAsync(rootFolderId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return false;
        }

        var contentType = patch.ContentType ?? existing.ContentType;
        if (patch.ContentType is not null)
        {
            ValidateContentType(patch.ContentType, nameof(patch.ContentType));
        }

        var changed = false;
        var defaultsChanged = false;
        var allowEmptyDefaults = false;
        var requestedDefaults = (IReadOnlyDictionary<string, RootFolderDefault>)existing.Defaults;
        if (patch.Defaults is not null)
        {
            requestedDefaults = patch.Defaults;
            defaultsChanged = true;
            changed = true;
        }
        else if (patch.ContentType is not null &&
            !string.Equals(patch.ContentType, existing.ContentType, StringComparison.Ordinal))
        {
            requestedDefaults = new Dictionary<string, RootFolderDefault>();
            defaultsChanged = true;
            allowEmptyDefaults = true;
            changed = true;
        }

        if (defaultsChanged)
        {
            var defaults = requestedDefaults.Count == 0 && allowEmptyDefaults
                ? []
                : await ValidateDefaultsAsync(
                    contentType,
                    requestedDefaults,
                    cancellationToken).ConfigureAwait(false);
            existing.Defaults = defaults;
        }

        if (patch.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(patch.Name))
            {
                throw new ArgumentException("Name is required.", nameof(patch.Name));
            }

            existing.Name = patch.Name;
            changed = true;
        }

        if (patch.Path is not null)
        {
            if (string.IsNullOrWhiteSpace(patch.Path))
            {
                throw new ArgumentException("Path is required.", nameof(patch.Path));
            }

            if (!Path.IsPathFullyQualified(patch.Path))
            {
                throw new ArgumentException("Path must be absolute.", nameof(patch.Path));
            }

            var fullPath = Path.TrimEndingDirectorySeparator(fileSystem.GetFullPath(patch.Path));
            if (!fileSystem.PathsEqual(existing.Path, fullPath))
            {
                if (!fileSystem.DirectoryExists(fullPath))
                {
                    throw new ArgumentException("Directory does not exist.", nameof(patch.Path));
                }

                var rootFolders = await rootFolderRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
                if (rootFolders.Any(rootFolder =>
                    rootFolder.Id != rootFolderId && fileSystem.PathsEqual(rootFolder.Path, fullPath)))
                {
                    throw new ArgumentException("Path is already configured as a root folder.", nameof(patch.Path));
                }
            }

            existing.Path = fullPath;
            changed = true;
        }

        if (patch.ContentType is not null)
        {
            existing.ContentType = patch.ContentType;
            changed = true;
        }

        if (patch.IsDefault is { } isDefault)
        {
            existing.IsDefault = isDefault;
            changed = true;
        }

        if (patch.DefaultMonitoring is { } monitoring)
        {
            existing.DefaultMonitoring = monitoring.Value;
            changed = true;
        }

        if (patch.RenameTemplate is { } renameTemplate)
        {
            existing.RenameTemplate = renameTemplate.Value;
            changed = true;
        }

        if (patch.UpgradeFileHandling is { } upgradeFileHandling)
        {
            existing.UpgradeFileHandling = upgradeFileHandling;
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        return await rootFolderRepository
            .UpdateAsync(existing, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(
        Guid rootFolderId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        return await rootFolderRepository
            .DeleteAsync(rootFolderId, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DiskSpaceResult> GetDiskSpaceAsync(CancellationToken cancellationToken = default)
    {
        var rootFolders = await rootFolderRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return BuildDiskSpaceResult(rootFolders, fileSystem);
    }
}
