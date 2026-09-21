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

using Windows.Win32;

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
            if (!TryGetPathCapacity(
                folder.Path,
                fileSystem,
                out var volumeName,
                out var folderTotalBytes,
                out var folderFreeBytes))
            {
                diskSpaceInfos.Add(CreateUnavailableDiskSpaceInfo(folder));
                continue;
            }

            diskSpaceInfos.Add(new DiskSpaceInfo
            {
                Id = folder.Id,
                Name = folder.Name,
                Path = folder.Path,
                TotalBytes = folderTotalBytes,
                FreeBytes = folderFreeBytes,
                UsedBytes = folderTotalBytes - folderFreeBytes
            });

            if (!volumes.Any(volume => fileSystem.PathsEqual(volume.Name, volumeName)))
            {
                volumes.Add((volumeName, folderTotalBytes, folderFreeBytes));
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

    private static bool TryGetPathCapacity(
        string path,
        IFileSystem fileSystem,
        out string volumeName,
        out long totalBytes,
        out long freeBytes)
    {
        volumeName = string.Empty;
        totalBytes = 0;
        freeBytes = 0;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = fileSystem.GetFullPath(path);
            if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
            {
                if (!PInvoke.GetDiskFreeSpaceEx(fullPath, out var free, out var total, out _))
                {
                    return false;
                }

                volumeName = Path.GetPathRoot(fullPath)!;
                totalBytes = (long)total;
                freeBytes = (long)free;
                return true;
            }

            var separator = Path.DirectorySeparatorChar;
            var candidate = fullPath.EndsWith(separator) ? fullPath : fullPath + separator;
            var mount = DriveInfo.GetDrives()
                .Where(drive => drive.IsReady && candidate.StartsWith(
                    drive.Name.EndsWith(separator) ? drive.Name : drive.Name + separator,
                    StringComparison.Ordinal))
                .MaxBy(drive => drive.Name.Length);

            if (mount is null)
            {
                return false;
            }

            volumeName = mount.Name;
            totalBytes = mount.TotalSize;
            freeBytes = mount.AvailableFreeSpace;
            return true;
        }
        catch
        {
            return false;
        }
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

    private void ValidateContentType(string contentType, string parameterName)
    {
        if (contentTypeRegistry.Exists(contentType))
        {
            return;
        }

        throw new ArgumentException($"The content type {contentType} is not registered.", parameterName);
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

        if (patch.ContentType is not null)
        {
            ValidateContentType(patch.ContentType.Value, nameof(patch.ContentType));
        }

        var contentType = patch.ContentType?.Value ?? existing.ContentType;
        var defaults = patch.Defaults;
        if (patch.Defaults is not null)
        {
            defaults = new()
            {
                Value = await ValidateDefaultsAsync(
                    contentType,
                    patch.Defaults.Value,
                    cancellationToken).ConfigureAwait(false)
            };
        }
        else if (patch.ContentType is not null &&
            !string.Equals(patch.ContentType.Value, existing.ContentType, StringComparison.Ordinal))
        {
            defaults = new() { Value = [] };
        }

        if (patch.Name is not null && string.IsNullOrWhiteSpace(patch.Name.Value))
        {
            throw new ArgumentException("Name is required.", nameof(patch.Name));
        }

        var path = patch.Path;
        if (patch.Path is not null)
        {
            if (string.IsNullOrWhiteSpace(patch.Path.Value))
            {
                throw new ArgumentException("Path is required.", nameof(patch.Path));
            }

            if (!Path.IsPathFullyQualified(patch.Path.Value))
            {
                throw new ArgumentException("Path must be absolute.", nameof(patch.Path));
            }

            var fullPath = Path.TrimEndingDirectorySeparator(fileSystem.GetFullPath(patch.Path.Value));
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

            path = new() { Value = fullPath };
        }

        if (patch is
            {
                Name: null,
                Path: null,
                ContentType: null,
                IsDefault: null,
                DefaultMonitoring: null,
                RenameTemplate: null,
                UpgradeFileHandling: null,
                Defaults: null
            })
        {
            return false;
        }

        return await rootFolderRepository
            .PatchAsync(
                patch with { Id = rootFolderId, Path = path, Defaults = defaults },
                actingUserId,
                cancellationToken)
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
