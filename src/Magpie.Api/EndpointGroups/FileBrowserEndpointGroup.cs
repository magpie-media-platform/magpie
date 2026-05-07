using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Models.Api;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Magpie.Api.EndpointGroups;

internal sealed class FileBrowserEndpointGroup(ILogger<FileBrowserEndpointGroup> logger) : IApiEndpointGroup
{
    public string Name => "file-browser";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapPost("contents", BrowseDirectory)
            .WithName(nameof(BrowseDirectory))
            .Produces<FileSystemEntry[]>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAdmin();
    }

    private Task<IResult> BrowseDirectory(
        FileBrowserRequest request,
        CancellationToken cancellationToken)
    {
        var path = NormalizePath(request.Path);

        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(Results.BadRequest("Path cannot be empty"));
        }

        try
        {
            if (!Directory.Exists(path))
            {
                return Task.FromResult(Results.BadRequest($"Directory does not exist: {path}"));
            }

            var entries = GetDirectoryEntries(path);
            return Task.FromResult(Results.Ok(entries));
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied to directory {Path}", path);
            return Task.FromResult(Results.BadRequest($"Access denied: {path}"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error browsing directory {Path}", path);
            return Task.FromResult(Results.BadRequest($"Error browsing directory: {ex.Message}"));
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return GetSystemRoot();
        }

        path = path.Trim();

        if (path == "/" || path == "\\")
        {
            return GetSystemRoot();
        }

        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string GetSystemRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? "C:\\";
        }

        return "/";
    }

    private static FileSystemEntry[] GetDirectoryEntries(string path)
    {
        var entries = new List<FileSystemEntry>();

        var directoryInfo = new DirectoryInfo(path);

        foreach (var directory in directoryInfo.EnumerateDirectories().OrderBy(d => d.Name))
        {
            try
            {
                entries.Add(new FileSystemEntry
                {
                    Name = directory.Name,
                    Path = directory.FullName,
                    Type = "folder"
                });
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        foreach (var file in directoryInfo.EnumerateFiles().OrderBy(f => f.Name))
        {
            try
            {
                entries.Add(new FileSystemEntry
                {
                    Name = file.Name,
                    Path = file.FullName,
                    Type = "file",
                    Size = file.Length
                });
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return [.. entries];
    }
}
