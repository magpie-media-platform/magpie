using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Storage;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Magpie.Core;

internal sealed class FileSystem(
    IBlockedFileTypeRepository blockedFileTypeRepository,
    IMimeMappingRepository mimeMappingRepository,
    IMemoryCache cache,
    ILogger<FileSystem> logger) : IFileSystem
{
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

        return await cache.GetOrCreateAsync(
            CacheKey(normalized),
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                entry.Size = 1;
                var mapping = await mimeMappingRepository.GetByIdAsync(normalized, cancellationToken)
                    .ConfigureAwait(false);
                return mapping?.MimeType;
            }).ConfigureAwait(false);
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
        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories);
    }

    public void Copy(string sourcePath, string destinationPath)
    {
        File.Copy(sourcePath, destinationPath, overwrite: false);
    }

    public long GetFileSize(string path)
    {
        return new FileInfo(path).Length;
    }

    public void DeleteFile(string path)
    {
        File.Delete(path);
    }

    public Task<IReadOnlyList<FileSystemEntry>> BrowseAsync(
        string? path,
        CancellationToken cancellationToken = default)
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
                (ruleParts[1] == "*" ||
                    string.Equals(ruleParts[1], mimeParts[1], StringComparison.OrdinalIgnoreCase));
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

        var normalized = NormalizeExtension(extension)
            ?? throw new ArgumentException($"{extension} is not a valid file extension.", nameof(extension));

        var existing = await mimeMappingRepository.GetByIdAsync(normalized, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            var mapping = new MimeMapping { Extension = normalized, MimeType = mimeType };
            var created = await mimeMappingRepository
                .CreateAsync(mapping, actingUserId, cancellationToken)
                .ConfigureAwait(false);
            cache.Remove(CacheKey(normalized));
            return created;
        }

        existing.MimeType = mimeType;
        await mimeMappingRepository.UpdateAsync(existing, actingUserId, cancellationToken).ConfigureAwait(false);
        cache.Remove(CacheKey(normalized));
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
            logger.LogDebug("Extension {Extension} is not valid, so the MIME mapping deletion did not run", extension);
            return false;
        }

        var deleted = await mimeMappingRepository
            .DeleteAsync(normalized, actingUserId, cancellationToken)
            .ConfigureAwait(false);
        cache.Remove(CacheKey(normalized));
        return deleted;
    }
}
