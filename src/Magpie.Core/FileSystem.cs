using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Storage;
using Magpie.Sdk.Caching;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

using Windows.Win32;
using Windows.Win32.Storage.FileSystem;

namespace Magpie.Core;

internal sealed partial class FileSystem(
    IBlockedFileTypeRepository blockedFileTypeRepository,
    IMimeMappingRepository mimeMappingRepository,
    ICacheFactory cacheFactory,
    ILogger<FileSystem> logger) : IFileSystem
{
    private static readonly EnumerationOptions RecursiveEnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        MatchType = MatchType.Win32,
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
    };

    private readonly ICache _cache = cacheFactory.GetOrCreate(
        "mime-type",
        () => new CachePolicy
        {
            Freshness = TimeSpan.FromMinutes(5),
            Retention = TimeSpan.FromMinutes(5),
            MaximumEntries = 100_000,
        });

    private static string CacheKey(string normalizedExtension) => $"mime:{normalizedExtension}";

    private static string? NormalizeExtension(string extensionOrPath)
    {
        if (string.IsNullOrWhiteSpace(extensionOrPath))
        {
            return null;
        }

        var raw = extensionOrPath.Trim();
        var extension = raw.Contains('/', StringComparison.Ordinal) || raw.Contains('\\', StringComparison.Ordinal)
            ? Path.GetExtension(raw)
            : raw.StartsWith("*.", StringComparison.Ordinal)
                ? raw[1..]
                : raw.StartsWith(".", StringComparison.Ordinal)
                    ? raw
                    : "." + raw;

        return string.IsNullOrEmpty(extension) || extension.Length < 2
            ? null
            : extension.ToLowerInvariant();
    }

    public async Task<IReadOnlyList<BlockedFileType>> ListExclusionsAsync(CancellationToken cancellationToken = default)
    {
        var exclusions = await blockedFileTypeRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return [.. exclusions];
    }

    public async Task<BlockedFileType> CreateExclusionAsync(
        string pattern,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        if (pattern.Contains('/', StringComparison.Ordinal))
        {
            if (!IsWellFormedMimeType(pattern))
            {
                throw new ArgumentException($"{pattern} is not a valid MIME type.");
            }

            if (pattern.Contains('*', StringComparison.Ordinal))
            {
                throw new ArgumentException($"Wildcard MIME type {pattern} is not supported.");
            }
        }
        else if (!IsWellFormedExtension(pattern))
        {
            throw new ArgumentException($"{pattern} is not a valid file extension.");
        }

        var blocked = await blockedFileTypeRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        if (blocked.Any(entry => string.Equals(entry.Pattern, pattern, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"{pattern} is already a blocked file pattern.");
        }

        var exclusion = new BlockedFileType { Pattern = pattern };
        return await blockedFileTypeRepository
            .CreateAsync(exclusion, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteExclusionAsync(
        Guid exclusionId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        return await blockedFileTypeRepository
            .DeleteAsync(exclusionId, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MimeMapping>> ListMimeMappingsAsync(CancellationToken cancellationToken = default)
    {
        var mappings = await mimeMappingRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return [.. mappings];
    }

    public async Task<string?> LookupMimeTypeAsync(
        string extensionOrPath,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeExtension(extensionOrPath);
        if (normalized is null)
        {
            return null;
        }

        return await _cache.GetOrAddAsync(
            CacheKey(normalized),
            async token =>
            {
                var mapping = await mimeMappingRepository.GetByIdAsync(normalized, token)
                    .ConfigureAwait(false);
                return mapping?.MimeType;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    public IEnumerable<string> EnumerateFileSystemEntries(string path)
    {
        return Directory.EnumerateFileSystemEntries(path);
    }

    public void CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    public IEnumerable<string> EnumerateFiles(string path)
    {
        return Directory.EnumerateFiles(path, "*", RecursiveEnumerationOptions);
    }

    public void Copy(string sourcePath, string destinationPath)
    {
        File.Copy(sourcePath, destinationPath, overwrite: false);
    }

    public long GetFileSize(string path)
    {
        return new FileInfo(path).Length;
    }

    /// <inheritdoc />
    public FileState ReadFileState(string path)
    {
        var file = new FileInfo(path);
        var size = file.Length;
        using var readableFile = File.OpenRead(path);
        return new()
        {
            Size = size,
            LastWriteTimeUtc = file.LastWriteTimeUtc
        };
    }

    public void DeleteFile(string path)
    {
        File.Delete(path);
    }

    /// <inheritdoc />
    public void DeleteEmptyDirectory(string path)
    {
        Directory.Delete(path, recursive: false);
    }

    public Task<IReadOnlyList<FileSystemEntry>> BrowseAsync(string? path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = NormalizeBrowsePath(path);
        if (!Directory.Exists(target))
        {
            throw new DirectoryNotFoundException($"Directory {target} does not exist.");
        }

        var directory = new DirectoryInfo(target);
        var entries = directory.EnumerateDirectories()
            .OrderBy(entry => entry.Name)
            .Select(entry => new FileSystemEntry
            {
                Name = entry.Name,
                Path = entry.FullName,
                Type = "folder"
            })
            .Concat(directory.EnumerateFiles()
                .OrderBy(entry => entry.Name)
                .Select(entry => new FileSystemEntry
                {
                    Name = entry.Name,
                    Path = entry.FullName,
                    Type = "file",
                    Size = entry.Length
                }))
            .ToList();

        return Task.FromResult<IReadOnlyList<FileSystemEntry>>(entries);
    }

    public string GetFullPath(string path)
    {
        return Path.GetFullPath(path);
    }

    public string GetRelativePath(string relativeTo, string path)
    {
        return Path.GetRelativePath(relativeTo, path);
    }

    public bool PathsEqual(string left, string right)
    {
        return Path.GetRelativePath(left, right) == ".";
    }

    public bool IsPathWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) &&
            relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    public bool PathContainsLink(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (relative == ".")
        {
            return false;
        }

        var current = Path.GetFullPath(root);
        foreach (var segment in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return false;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }

    public bool HasPathPrefix(string path, string prefix)
    {
        var trimmed = prefix.TrimEnd('\\', '/');
        return trimmed.Length > 0 && path.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) &&
            (path.Length == trimmed.Length || path[trimmed.Length] is '\\' or '/');
    }

    public string ReplacePathPrefix(string path, string prefix, string replacement)
    {
        var trimmedPrefix = prefix.TrimEnd('\\', '/');
        var trimmedReplacement = replacement.TrimEnd('\\', '/');
        var remainder = path.Length > trimmedPrefix.Length ? path[trimmedPrefix.Length..] : string.Empty;
        if (remainder.Length == 0)
        {
            return trimmedReplacement;
        }

        var forwardSlash = trimmedReplacement.Contains('/', StringComparison.Ordinal) &&
            !trimmedReplacement.Contains('\\', StringComparison.Ordinal);
        var normalizedRemainder = forwardSlash ? remainder.Replace('\\', '/') : remainder.Replace('/', '\\');
        return trimmedReplacement + (forwardSlash ? '/' : '\\') + normalizedRemainder.TrimStart('\\', '/');
    }

    public Stream OpenRead(string path)
    {
        return File.OpenRead(path);
    }

    public Stream CreateFile(string path)
    {
        return File.Create(path);
    }

    public void Move(string sourcePath, string destinationPath, bool overwrite = false)
    {
        File.Move(sourcePath, destinationPath, overwrite);
    }

    private static string NormalizeBrowsePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Trim() is "/" or "\\")
        {
            return OperatingSystem.IsWindows()
                ? Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\"
                : "/";
        }

        return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public async Task<bool> MatchesAnyFileTypeAsync(
        string filePath,
        IReadOnlyList<string>? fileTypes,
        CancellationToken cancellationToken = default)
    {
        var mimeType = fileTypes?.Any(fileType => fileType.Contains('/', StringComparison.Ordinal)) == true
            ? await LookupMimeTypeAsync(filePath, cancellationToken).ConfigureAwait(false)
            : null;
        return MatchesAnyFileType(filePath, mimeType, fileTypes);
    }

    /// <summary>
    /// Gets whether a file is a primary file of a level, which is the file that fulfills the level.
    /// </summary>
    /// <param name="fileSystem">The file system that matches file types</param>
    /// <param name="level">The level that owns the file</param>
    /// <param name="file">The file to classify</param>
    internal static bool IsPrimaryFile(IFileSystem fileSystem, ContentHierarchyLevelDefinition level, MediaFile file) =>
        level.AcceptedFileTypes.Count > 0
            ? fileSystem.MatchesAnyFileType(file.FilePath, file.MimeType, level.AcceptedFileTypes)
            : !fileSystem.MatchesAnyFileType(file.FilePath, file.MimeType, level.AcceptedSidecarTypes);

    public bool MatchesAnyFileType(string filePath, string? mimeType, IReadOnlyList<string>? fileTypes)
    {
        return fileTypes is not null &&
            !string.IsNullOrWhiteSpace(filePath) &&
            fileTypes.Any(fileType => MatchesRule(filePath, mimeType, fileType));
    }

    private static bool MatchesRule(string filePath, string? mimeType, string rule)
    {
        if (string.IsNullOrWhiteSpace(rule))
        {
            return false;
        }

        if (rule.Contains('/', StringComparison.Ordinal))
        {
            if (mimeType is null)
            {
                return false;
            }

            var ruleParts = rule.Split('/', 2);
            var mimeParts = mimeType.Split('/', 2);
            return ruleParts.Length == 2 &&
                mimeParts.Length == 2 &&
                (ruleParts[0] == "*" ||
                    string.Equals(ruleParts[0], mimeParts[0], StringComparison.OrdinalIgnoreCase)) &&
                (ruleParts[1] == "*" || string.Equals(ruleParts[1], mimeParts[1], StringComparison.OrdinalIgnoreCase));
        }

        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(extension))
        {
            return false;
        }

        var normalizedRule = rule.StartsWith("*.", StringComparison.Ordinal)
            ? rule[1..]
            : rule.StartsWith(".", StringComparison.Ordinal) ? rule : "." + rule;
        return string.Equals(normalizedRule, extension, StringComparison.OrdinalIgnoreCase);
    }

    public FileKind ResolveFileKind(string? mimeType)
    {
        if (mimeType is null)
        {
            return FileKind.Unknown;
        }

        var normalized = mimeType.ToLowerInvariant();
        var slash = normalized.IndexOf('/', StringComparison.Ordinal);
        var topLevel = slash < 0 ? normalized : normalized[..slash];
        return topLevel switch
        {
            "video" => FileKind.Video,
            "audio" => FileKind.Audio,
            _ => normalized switch
            {
                "application/x-subrip" or "text/x-ssa" or "text/vtt" or "text/x-microdvd" => FileKind.Subtitle,
                _ => FileKind.Unknown,
            },
        };
    }

    private static bool IsWellFormedMimeType(string mimeType)
    {
        return MediaTypeHeaderValue.TryParse(mimeType, out var parsed) &&
            parsed.Parameters.Count == 0 &&
            string.Equals(parsed.MediaType, mimeType, StringComparison.OrdinalIgnoreCase) &&
            mimeType.Split('/') is [{ Length: > 0 }, { Length: > 0 }];
    }

    private static bool IsWellFormedExtension(string extension)
    {
        var name = extension.StartsWith("*.", StringComparison.Ordinal)
            ? extension[2..]
            : extension.StartsWith('.') ? extension[1..] : extension;
        return name.Length > 0 &&
            !name.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) ||
                "\\/:*?\"<>|.".Contains(character));
    }

    public async Task<MimeMapping> UpsertMimeMappingAsync(
        string extension,
        string mimeType,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            throw new ArgumentException("MIME type must not be empty.", nameof(mimeType));
        }

        mimeType = mimeType.Trim();
        if (!IsWellFormedMimeType(mimeType))
        {
            throw new ArgumentException($"{mimeType} is not a valid MIME type.", nameof(mimeType));
        }

        var normalized = NormalizeExtension(extension)
            ?? throw new ArgumentException($"{extension} is not a valid file extension.", nameof(extension));

        var existing = await mimeMappingRepository.GetByIdAsync(normalized, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            var mapping = new MimeMapping { Extension = normalized, MimeType = mimeType };
            var created = await mimeMappingRepository
                .CreateAsync(mapping, actingUserId, cancellationToken)
                .ConfigureAwait(false);
            _cache.Remove(CacheKey(normalized));
            return created;
        }

        existing.MimeType = mimeType;
        await mimeMappingRepository.UpdateAsync(existing, actingUserId, cancellationToken).ConfigureAwait(false);
        _cache.Remove(CacheKey(normalized));
        return existing;
    }

    public async Task<bool> DeleteMimeMappingAsync(
        string extension,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeExtension(extension);
        if (normalized is null)
        {
            logger.LogDebug("Extension {Extension} is not valid", extension);
            return false;
        }

        var deleted = await mimeMappingRepository
            .DeleteAsync(normalized, actingUserId, cancellationToken)
            .ConfigureAwait(false);
        _cache.Remove(CacheKey(normalized));
        return deleted;
    }

    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int ReadLinuxFileIdentity(
        int directory,
        string path,
        int flags,
        uint mask,
        out LinuxFileIdentity identity);

    /// <inheritdoc />
    public string? GetFileIdentity(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                return null;
            }

            if (OperatingSystem.IsWindowsVersionAtLeast(6, 2))
            {
                using var handle = PInvoke.CreateFile(
                    path,
                    0,
                    FILE_SHARE_MODE.FILE_SHARE_READ |
                        FILE_SHARE_MODE.FILE_SHARE_WRITE |
                        FILE_SHARE_MODE.FILE_SHARE_DELETE,
                    null,
                    FILE_CREATION_DISPOSITION.OPEN_EXISTING,
                    default,
                    null);
                Span<byte> identity = stackalloc byte[24];
                return handle.IsInvalid || !PInvoke.GetFileInformationByHandleEx(
                    handle,
                    FILE_INFO_BY_HANDLE_CLASS.FileIdInfo,
                    identity) ? null : "windows:" + Convert.ToHexString(identity);
            }

            if (OperatingSystem.IsLinux() && ReadLinuxFileIdentity(-100, path, 0x100, 0x900, out var linux) == 0 &&
                (linux.Mask & 0x100) == 0x100)
            {
                var birth = (linux.Mask & 0x800) == 0x800
                    ? $":{linux.BirthSeconds:X}:{linux.BirthNanoseconds:X}"
                    : string.Empty;
                return $"linux:{linux.DeviceMajor:X}:{linux.DeviceMinor:X}:{linux.Inode:X}{birth}";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or
            EntryPointNotFoundException or DllNotFoundException)
        {
            logger.LogDebug(ex, "File identity is unavailable for {Path}", path);
        }

        return null;
    }

    /// <inheritdoc />
    public string? GetFileVersion(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return $"{file.Length:X}:{file.LastWriteTimeUtc.Ticks:X}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger.LogDebug(ex, "File version is unavailable for {Path}", path);
            return null;
        }
    }

    /// <inheritdoc />
    public IDisposable AcquireImportPaths(IReadOnlyList<string> paths)
    {
        var locks = new List<ImportPathLock>();
        var handles = new ImportPathHandles(locks);
        try
        {
            foreach (var path in paths.Select(path => ResolveLinks(Path.GetFullPath(path)).Path)
                .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .Order(StringComparer.Ordinal))
            {
                var directory = Path.Combine(Path.GetDirectoryName(path)!, ".magpie-import-locks");
                var name = OperatingSystem.IsWindows() ?
                    Path.GetFileName(path).ToUpperInvariant() :
                    Path.GetFileName(path);
                var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)));
                locks.Add(OpenImportPathLock(Path.Combine(directory, key + ".lock")));
            }

            return handles;
        }
        catch
        {
            handles.Dispose();
            throw;
        }
    }

    private static ImportPathLock OpenImportPathLock(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            FileStream stream;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                stream = new(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    OperatingSystem.IsWindows() ? FileShare.Delete : FileShare.None);
            }
            catch (Exception ex) when (attempt < 3 && ex is DirectoryNotFoundException or UnauthorizedAccessException)
            {
                continue;
            }

            if (OperatingSystem.IsWindows())
            {
                return new(stream, path, Removable: true);
            }

            var linked = IsLinkedLinuxFile(stream, path);
            if (linked != false)
            {
                return new(stream, path, Removable: linked == true);
            }

            stream.Dispose();
            if (attempt >= 3)
            {
                throw new IOException("The import path lock changed during acquisition.");
            }
        }
    }

    private static bool? IsLinkedLinuxFile(FileStream stream, string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        try
        {
            if (ReadLinuxFileIdentity((int)stream.SafeFileHandle.DangerousGetHandle(), string.Empty, 0x1000, 0x100,
                    out var held) != 0 || (held.Mask & 0x100) == 0)
            {
                return null;
            }

            return ReadLinuxFileIdentity(-100, path, 0x100, 0x100, out var current) == 0 &&
                (current.Mask & 0x100) != 0 && held.Inode == current.Inode &&
                held.DeviceMajor == current.DeviceMajor && held.DeviceMinor == current.DeviceMinor;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task TransferToOwnedPathAsync(
        string source,
        string destination,
        ImportMode mode,
        Func<string, Task> onCreated,
        CancellationToken cancellationToken = default) =>
        TransferToOwnedPathAsync(this, source, destination, mode, onCreated, cancellationToken);

    internal static async Task TransferToOwnedPathAsync(
        IFileSystem observations,
        string source,
        string destination,
        ImportMode mode,
        Func<string, Task> onCreated,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourceIdentity = observations.GetFileIdentity(source) ??
            throw new FileOwnershipUnavailableException(
                "The source file identity is unavailable, so the import cannot confirm file ownership.");
        var capacity = observations.GetStorageCapacity(destination) ??
            throw new StorageCapacityUnavailableException(null);
        var sourceVolume = observations.GetStorageCapacity(source)?.VolumeId;
        var destinationVolume = capacity.VolumeId;
        if (mode == ImportMode.Move &&
            (string.IsNullOrWhiteSpace(sourceVolume) || string.IsNullOrWhiteSpace(destinationVolume)))
        {
            throw new StorageCapacityUnavailableException(capacity.AvailableBytes);
        }

        if (mode == ImportMode.Move && sourceVolume == destinationVolume)
        {
            await onCreated(sourceIdentity).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(source, destination, overwrite: false);
            return;
        }

        var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var recorded = false;
        try
        {
            await using (output.ConfigureAwait(false))
            {
                var identity = observations.GetFileIdentity(destination) ??
                    throw new FileOwnershipUnavailableException(
                        "The destination file identity is unavailable, so the import cannot confirm file ownership.");
                await onCreated(identity).ConfigureAwait(false);
                recorded = true;
                var input = new FileStream(
                    source,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var inputHandle = input.ConfigureAwait(false);
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch when (!recorded)
        {
            File.Delete(destination);
            throw;
        }
    }

    /// <inheritdoc />
    public StorageCapacity? GetStorageCapacity(string path)
    {
        try
        {
            var (resolvedPath, _) = ResolveLinks(Path.GetFullPath(path));
            while (!Directory.Exists(resolvedPath))
            {
                resolvedPath = Path.GetDirectoryName(resolvedPath);
                if (string.IsNullOrEmpty(resolvedPath))
                {
                    return null;
                }
            }

            if (OperatingSystem.IsWindowsVersionAtLeast(6, 2))
            {
                if (!PInvoke.GetDiskFreeSpaceEx(resolvedPath, out var free, out var total, out _) ||
                    free > long.MaxValue ||
                    total > long.MaxValue)
                {
                    return null;
                }

                return new()
                {
                    VolumeId = ReadWindowsCapacityVolumeId(resolvedPath),
                    AvailableBytes = (long)free,
                    TotalBytes = (long)total
                };
            }

            var candidate = resolvedPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var mount = DriveInfo.GetDrives()
                .Where(drive => candidate.StartsWith(
                    drive.Name.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
                .MaxBy(drive => drive.Name.Length);
            return mount is null || mount.AvailableFreeSpace < 0 ? null : new StorageCapacity
            {
                VolumeId = mount.Name,
                AvailableBytes = mount.AvailableFreeSpace,
                TotalBytes = mount.TotalSize
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Storage capacity is unavailable for {Path}", path);
            return null;
        }
    }

    [SupportedOSPlatform("windows6.2")]
    private static string? ReadWindowsCapacityVolumeId(string path)
    {
        Span<char> mountPath = new char[Math.Max(path.Length + 2, 260)];
        Span<char> volumeName = stackalloc char[50];
        if (PInvoke.GetVolumePathName(path, mountPath) && PInvoke.GetVolumeNameForVolumeMountPoint(
                mountPath[..mountPath.IndexOf('\0')].ToString(),
                volumeName))
        {
            return volumeName[..volumeName.IndexOf('\0')].ToString().ToUpperInvariant();
        }

        using var handle = PInvoke.CreateFile(
            path,
            0,
            FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_DELETE,
            null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS,
            null);
        if (handle.IsInvalid)
        {
            return null;
        }

        Span<char> finalPath = new char[32768];
        var length = PInvoke.GetFinalPathNameByHandle(
            handle,
            finalPath,
            GETFINALPATHNAMEBYHANDLE_FLAGS.FILE_NAME_OPENED);
        if (length == 0 || length >= finalPath.Length)
        {
            return null;
        }

        var root = Path.GetPathRoot(finalPath[..(int)length].ToString())?.ToUpperInvariant();
        Span<byte> identity = stackalloc byte[24];
        if (root?.StartsWith("\\\\?\\UNC\\", StringComparison.Ordinal) != true ||
            !PInvoke.GetFileInformationByHandleEx(handle, FILE_INFO_BY_HANDLE_CLASS.FileIdInfo, identity))
        {
            return null;
        }

        var serial = BinaryPrimitives.ReadUInt64LittleEndian(identity);
        return serial == 0 ? null : $"{root}:{serial:X16}";
    }

    private static (string Path, List<string> Links) ResolveLinks(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath)!;
        var current = root;
        var links = new List<string>();
        foreach (var segment in fullPath[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                var target = (attributes & FileAttributes.Directory) != 0
                    ? Directory.ResolveLinkTarget(current, returnFinalTarget: true)
                    : File.ResolveLinkTarget(current, returnFinalTarget: true);
                var targetPath = target?.FullName ?? throw new IOException("The storage link could not be resolved.");
                links.Add(current);
                links.Add(targetPath);
                current = targetPath;
            }
        }

        return (current, links);
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxFileIdentity
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(80)] public long BirthSeconds;
        [FieldOffset(88)] public uint BirthNanoseconds;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }

    private readonly record struct ImportPathLock(FileStream Stream, string Path, bool Removable);

    internal sealed class StorageCapacityUnavailableException(long? availableBytes)
        : IOException("The transfer capacity check is unavailable.")
    {
        internal long? AvailableBytes { get; } = availableBytes;
    }

    internal sealed class FileOwnershipUnavailableException(string message) : IOException(message);

    private sealed class ImportPathHandles(List<ImportPathLock> locks) : IDisposable
    {
        public void Dispose()
        {
            foreach (var held in locks)
            {
                if (held.Removable)
                {
                    TryRemove(() => File.Delete(held.Path));
                }

                held.Stream.Dispose();
                if (held.Removable)
                {
                    TryRemove(() => Directory.Delete(System.IO.Path.GetDirectoryName(held.Path)!, recursive: false));
                }
            }
        }

        private static void TryRemove(Action remove)
        {
            try
            {
                remove();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
