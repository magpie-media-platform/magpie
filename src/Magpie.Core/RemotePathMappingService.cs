using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Services;

namespace Magpie.Core;

internal sealed class RemotePathMappingService(
    IRemotePathMappingRepository repository,
    IFileSystem fileSystem) : IRemotePathMappingService
{
    private static readonly string[] HostSettingKeys =
        ["host", "url", "baseUrl", "baseurl", "server", "hostname", "address"];

    private static ConfigurationTestResult Failure(
        string message,
        ConfigurationFailureKind kind = ConfigurationFailureKind.Fatal) => new()
        {
            Success = false,
            Message = message,
            FailureKind = kind
        };

    public async Task<IReadOnlyList<RemotePathMapping>> ListAsync(CancellationToken cancellationToken = default)
    {
        return [.. await repository.ListAllAsync(cancellationToken).ConfigureAwait(false)];
    }

    public Task<RemotePathMapping?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return repository.GetByIdAsync(id, cancellationToken);
    }

    public Task<RemotePathMapping> CreateAsync(
        RemotePathMappingCreate create,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);

        return repository.CreateAsync(
            new RemotePathMapping
            {
                Host = RequireHost(create.Host),
                RemotePath = Require(create.RemotePath, "Remote path"),
                LocalPath = Require(create.LocalPath, "Local path")
            },
            actingUserId,
            cancellationToken);
    }

    public async Task<bool> PatchAsync(
        Guid id,
        RemotePathMappingPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var normalized = patch with
        {
            Id = id,
            Host = patch.Host is null ? null : new() { Value = RequireHost(patch.Host.Value) },
            RemotePath = patch.RemotePath is null
                ? null
                : new() { Value = Require(patch.RemotePath.Value, "Remote path") },
            LocalPath = patch.LocalPath is null
                ? null
                : new() { Value = Require(patch.LocalPath.Value, "Local path") }
        };

        return await repository.PatchAsync(normalized, actingUserId, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> DeleteAsync(Guid id, Guid? actingUserId, CancellationToken cancellationToken = default)
    {
        return repository.DeleteAsync(id, actingUserId, cancellationToken);
    }

    public Task<ConfigurationTestResult> TestLocalPathAsync(
        string localPath,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(TestLocalPath(localPath));
    }

    public string? GetHost(IReadOnlyDictionary<string, string>? settings)
    {
        if (settings is null)
        {
            return null;
        }

        foreach (var key in HostSettingKeys)
        {
            if (settings.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
            {
                return NormalizeHost(raw);
            }
        }

        return null;
    }

    public string Map(string remotePath, string? clientHost, IEnumerable<RemotePathMapping> mappings)
    {
        if (remotePath.Length == 0)
        {
            return remotePath;
        }

        var best = mappings
            .Where(mapping => !string.IsNullOrWhiteSpace(mapping.RemotePath))
            .Where(mapping => clientHost is null || string.IsNullOrWhiteSpace(mapping.Host) ||
                string.Equals(NormalizeHost(mapping.Host), clientHost, StringComparison.OrdinalIgnoreCase))
            .Where(mapping => fileSystem.HasPathPrefix(remotePath, mapping.RemotePath))
            .MaxBy(mapping => TrimTrailingSeparators(mapping.RemotePath).Length);
        if (best is null)
        {
            return remotePath;
        }

        return fileSystem.ReplacePathPrefix(remotePath, best.RemotePath, best.LocalPath);
    }

    private static string Require(string? value, string name)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrEmpty(trimmed) ? throw new ArgumentException($"{name} is required.") : trimmed;
    }

    private static string RequireHost(string? value)
    {
        var trimmed = Require(value, "Host");

        return NormalizeHost(trimmed) is null
            ? throw new ArgumentException("Host must include a hostname.")
            : trimmed;
    }

    private static string? NormalizeHost(string raw)
    {
        var value = raw.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            return TrimIpv6Brackets(uri.Host);
        }

        if (value.Contains("://", StringComparison.Ordinal))
        {
            return null;
        }

        var hostPart = value;
        var slash = hostPart.IndexOf('/', StringComparison.Ordinal);
        if (slash >= 0)
        {
            hostPart = hostPart[..slash];
        }

        if (hostPart.StartsWith("[", StringComparison.Ordinal))
        {
            var end = hostPart.IndexOf(']', StringComparison.Ordinal);
            return end > 1 ? TrimIpv6Brackets(hostPart[..(end + 1)]) : null;
        }

        if (IPAddress.TryParse(hostPart, out _))
        {
            return hostPart;
        }

        var colon = hostPart.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            hostPart = hostPart[..colon];
        }

        hostPart = hostPart.Trim();
        return string.IsNullOrWhiteSpace(hostPart) ? null : hostPart;
    }

    private static string TrimTrailingSeparators(string value) => value.TrimEnd('\\', '/');

    private static string TrimIpv6Brackets(string host) =>
        host.StartsWith("[", StringComparison.Ordinal) && host.EndsWith("]", StringComparison.Ordinal)
            ? host[1..^1]
            : host;

    private ConfigurationTestResult TestLocalPath(string localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return Failure("Local path is required.");
        }

        var path = localPath.Trim();
        try
        {
            if (!fileSystem.DirectoryExists(path))
            {
                return Failure("Local path does not exist or is not a directory.", ConfigurationFailureKind.NotFound);
            }

            using var enumerator = fileSystem.EnumerateFileSystemEntries(path).GetEnumerator();
            _ = enumerator.MoveNext();
            return new()
            {
                Success = true,
                Message = "Local path is reachable and readable.",
                FailureKind = ConfigurationFailureKind.None
            };
        }
        catch (UnauthorizedAccessException)
        {
            return Failure("Local path exists but Magpie cannot read it.", ConfigurationFailureKind.Auth);
        }
        catch (SecurityException)
        {
            return Failure("Local path exists but Magpie cannot read it.", ConfigurationFailureKind.Auth);
        }
        catch (DirectoryNotFoundException)
        {
            return Failure("Local path does not exist or is not a directory.", ConfigurationFailureKind.NotFound);
        }
        catch (PathTooLongException)
        {
            return Failure("Local path is too long.");
        }
        catch (NotSupportedException)
        {
            return Failure("Local path format is not supported.");
        }
        catch (ArgumentException)
        {
            return Failure("Local path format is invalid.");
        }
        catch (IOException)
        {
            return Failure("Local path exists but Magpie cannot read it.", ConfigurationFailureKind.Auth);
        }
    }
}
