using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Storage;

namespace Magpie.Sdk.Services;

/// <summary>
/// The file-system and file-type operations of the host.
/// </summary>
public interface IFileSystem
{
    /// <summary>
    /// Determines whether a file exists at a path.
    /// </summary>
    /// <param name="path">The path of the file to find</param>
    bool FileExists(string path);

    /// <summary>
    /// Determines whether a directory exists at a path.
    /// </summary>
    /// <param name="path">The path of the directory to find</param>
    bool DirectoryExists(string path);

    /// <summary>
    /// Lists the entries directly in a directory.
    /// </summary>
    /// <param name="path">The path of the directory to read</param>
    IEnumerable<string> EnumerateFileSystemEntries(string path);

    /// <summary>
    /// Creates a directory.
    /// </summary>
    /// <param name="path">The path of the directory to create</param>
    void CreateDirectory(string path);

    /// <summary>
    /// Lists every file in a directory and its subdirectories.
    /// </summary>
    /// <param name="path">The path of the directory to read</param>
    IEnumerable<string> EnumerateFiles(string path);

    /// <summary>
    /// Copies a file to a new path.
    /// </summary>
    /// <param name="sourcePath">The path of the file to copy</param>
    /// <param name="destinationPath">The path of the new copy</param>
    void Copy(string sourcePath, string destinationPath);

    /// <summary>
    /// Gets the size of a file in bytes.
    /// </summary>
    /// <param name="path">The path of the file to measure</param>
    long GetFileSize(string path);

    /// <summary>
    /// Deletes a file.
    /// </summary>
    /// <param name="path">The path of the file to delete</param>
    void DeleteFile(string path);

    /// <summary>
    /// Lists the entries directly under a path.
    /// </summary>
    /// <param name="path">The directory to list, or <see langword="null"/> for the file-system root</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<FileSystemEntry>> BrowseAsync(
        string? path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the file-type exclusion patterns.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<BlockedFileType>> ListExclusionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a file-type exclusion pattern.
    /// </summary>
    /// <param name="pattern">The pattern that matches the file names to exclude</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<BlockedFileType> CreateExclusionAsync(
        string pattern,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a file-type exclusion pattern.
    /// </summary>
    /// <param name="exclusionId">The id of the exclusion to delete</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteExclusionAsync(
        Guid exclusionId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the mappings from file extension to MIME type.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<MimeMapping>> ListMimeMappingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the MIME type for a file extension or path.
    /// </summary>
    /// <param name="extensionOrPath">The file extension, or a path that ends with one</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<string?> LookupMimeTypeAsync(
        string extensionOrPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether a file matches any configured file type.
    /// </summary>
    /// <param name="filePath">The file path to test</param>
    /// <param name="fileTypes">The file extensions or MIME types to test</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> MatchesAnyFileTypeAsync(
        string filePath,
        IReadOnlyList<string>? fileTypes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether a file path and known MIME type match any configured file type.
    /// </summary>
    /// <param name="filePath">The file path to test</param>
    /// <param name="mimeType">The MIME type of the file, or <see langword="null"/> when unknown</param>
    /// <param name="fileTypes">The file extensions or MIME types to test</param>
    bool MatchesAnyFileType(string filePath, string? mimeType, IReadOnlyList<string>? fileTypes);

    /// <summary>
    /// Resolves the file kind represented by a MIME type.
    /// </summary>
    /// <param name="mimeType">The MIME type, or <see langword="null"/> when unknown</param>
    FileKind ResolveFileKind(string? mimeType);

    /// <summary>
    /// Adds or replaces the MIME type registration for a file extension.
    /// </summary>
    /// <param name="extension">The file extension to map</param>
    /// <param name="mimeType">The MIME type to store for the extension</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<MimeMapping> UpsertMimeMappingAsync(
        string extension,
        string mimeType,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the MIME mapping of a file extension.
    /// </summary>
    /// <param name="extension">The file extension to unmap</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteMimeMappingAsync(
        string extension,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the absolute path of a path.
    /// </summary>
    /// <param name="path">The path to resolve</param>
    string GetFullPath(string path);

    /// <summary>
    /// A path relative to another path.
    /// </summary>
    /// <param name="relativeTo">The path that the result is relative to</param>
    /// <param name="path">The path to resolve</param>
    string GetRelativePath(string relativeTo, string path);

    /// <summary>
    /// Determines whether two local paths identify the same location.
    /// </summary>
    /// <param name="left">The first path</param>
    /// <param name="right">The second path</param>
    bool PathsEqual(string left, string right);

    /// <summary>
    /// Determines whether a local path is within a root path.
    /// </summary>
    /// <param name="root">The root path</param>
    /// <param name="path">The path to test</param>
    bool IsPathWithin(string root, string path);

    /// <summary>
    /// Determines whether a path below a root contains a symbolic link or reparse point.
    /// </summary>
    /// <param name="root">The root path to exclude from the check</param>
    /// <param name="path">The path to check</param>
    bool PathContainsLink(string root, string path);

    /// <summary>
    /// Determines whether a remote path starts with a complete path prefix.
    /// </summary>
    /// <param name="path">The path to test</param>
    /// <param name="prefix">The path prefix</param>
    bool HasPathPrefix(string path, string prefix);

    /// <summary>
    /// Replaces a remote path prefix.
    /// </summary>
    /// <param name="path">The path to change</param>
    /// <param name="prefix">The path prefix to replace</param>
    /// <param name="replacement">The replacement path</param>
    string ReplacePathPrefix(string path, string prefix, string replacement);

    /// <summary>
    /// Opens a file for reading.
    /// </summary>
    /// <param name="path">The path of the file to open</param>
    Stream OpenRead(string path);

    /// <summary>
    /// Creates or replaces a file.
    /// </summary>
    /// <param name="path">The path of the file to create</param>
    Stream CreateFile(string path);

    /// <summary>
    /// Moves a file to a new path.
    /// </summary>
    /// <param name="sourcePath">The path of the file to move</param>
    /// <param name="destinationPath">The new path of the file</param>
    /// <param name="overwrite">Whether to replace an existing destination file</param>
    void Move(string sourcePath, string destinationPath, bool overwrite = false);
}
