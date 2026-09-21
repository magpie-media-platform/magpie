using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Config;
using Magpie.Core.Events;
using Magpie.Core.Observability;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Caching;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Extensions;

internal sealed class ExtensionService(
    IHttpClientFactory httpClientFactory,
    IExtensionRepository extensionRepository,
    IExtensionPackageSourceRepository packageSourceRepository,
    IExtensionActivationGenerationRepository activationGenerationRepository,
    MagpieHostEnvironment hostEnvironment,
    MagpieHostOptions hostOptions,
    IExtensionLifecycleManager lifecycleManager,
    IEventBus eventBus,
    ICacheFactory cacheFactory,
    IFileSystem fileSystem,
    TimeProvider timeProvider,
    ILogger<ExtensionService> logger) : IExtensionService
{
    public const string HttpClientName = "ExtensionService";

    private static readonly TimeSpan UpdateInfoCacheTtl = TimeSpan.FromMinutes(15);

    private static readonly string[] PreferredTargetFrameworks =
    [
        "net10.0",
        "net9.0",
        "net8.0",
        "netstandard2.1",
        "netstandard2.0"
    ];

    private static readonly string[] SupportedRegistrationTypes =
    [
        "RegistrationsBaseUrl/3.6.0",
        "RegistrationsBaseUrl/3.4.0",
        "RegistrationsBaseUrl/3.0.0-rc",
        "RegistrationsBaseUrl/3.0.0-beta",
        "RegistrationsBaseUrl"
    ];

    private const string UpdateInfoCacheKey = "magpie.extensions.update-availability";
    private const string ManualDisableReason = "Disabled by request.";
    private const string DefaultTagFilter = "magpie";

    private readonly ICache _updateInfoCache = cacheFactory.GetOrCreate(
        "extension-update-info",
        () => new CachePolicy
        {
            Freshness = UpdateInfoCacheTtl,
            Retention = UpdateInfoCacheTtl,
            MaximumEntries = 1,
        });

    private static Extension ExtractPackage(
        Stream packageStream,
        string targetDir,
        string directoryName,
        string? fallbackPackageId,
        bool managed,
        IFileSystem fileSystem,
        TimeProvider timeProvider,
        ILogger logger)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);

        var nuspec = ReadNuspec(archive, logger);
        var libDirectory = ChoosePreferredLibFolder(archive);

        var extractedAssemblyPath = ExtractLibFolder(archive, libDirectory, targetDir, fileSystem);
        ExtractNuspec(archive, targetDir, fileSystem);

        var packageId = nuspec?.Id ?? fallbackPackageId ?? directoryName;
        var packageVersion = nuspec?.Version ?? "0.0.0";
        var assemblyName = extractedAssemblyPath is null
            ? packageId
            : Path.GetFileNameWithoutExtension(extractedAssemblyPath);
        var filePath = extractedAssemblyPath ?? Path.Combine(targetDir, $"{packageId}.dll");

        return new()
        {
            Id = Guid.NewGuid(),
            PackageId = packageId,
            Name = nuspec?.Title ?? packageId,
            Author = nuspec?.Authors,
            Description = nuspec?.Description,
            Version = packageVersion,
            TargetABI = nuspec?.TargetAbi,
            FilePath = filePath,
            AssemblyName = assemblyName,
            Installed = true,
            Enabled = true,
            DisabledReason = null,
            LoadOrder = 1000,
            Managed = managed,
            NuGetId = managed ? packageId : null,
            PackageRepository = null,
            AutoUpgrade = false,
            LastChecked = timeProvider.GetUtcNow().UtcDateTime
        };
    }

    private static Extension BuildUpdatedExtension(
        Extension existing,
        Extension staged,
        string stagedDirectory,
        string targetDirectory,
        IFileSystem fileSystem)
    {
        var assemblyRelativePath = fileSystem.GetRelativePath(stagedDirectory, staged.FilePath);

        return staged with
        {
            Id = existing.Id,
            PackageId = existing.PackageId,
            LifecycleVersion = existing.LifecycleVersion,
            FilePath = Path.Combine(targetDirectory, assemblyRelativePath),
            Installed = true,
            Enabled = existing.Enabled,
            DisabledReason = existing.DisabledReason,
            LoadOrder = existing.LoadOrder,
            Managed = true,
            NuGetId = existing.PackageId,
            PackageRepository = existing.PackageRepository,
            AutoUpgrade = existing.AutoUpgrade,
            CreatedAt = existing.CreatedAt,
            CreatedByUserId = existing.CreatedByUserId
        };
    }

    private static Extension BuildStagedExtensionForTarget(
        Extension staged,
        string stagedDirectory,
        string targetDirectory,
        IFileSystem fileSystem)
    {
        var assemblyRelativePath = fileSystem.GetRelativePath(stagedDirectory, staged.FilePath);

        return staged with
        {
            FilePath = Path.Combine(targetDirectory, assemblyRelativePath)
        };
    }

    private static Extension BuildRegisteredExtension(Extension existing, Extension staged)
    {
        return staged with
        {
            Id = existing.Id,
            PackageId = existing.PackageId,
            LifecycleVersion = existing.LifecycleVersion,
            Installed = true,
            Enabled = existing.Enabled,
            DisabledReason = existing.DisabledReason,
            LoadOrder = existing.LoadOrder,
            PackageRepository = existing.PackageRepository,
            AutoUpgrade = existing.AutoUpgrade,
            CreatedAt = existing.CreatedAt,
            CreatedByUserId = existing.CreatedByUserId
        };
    }

    private static NuspecMetadata? ReadNuspec(ZipArchive archive, ILogger logger)
    {
        var entry = archive.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) &&
            !e.FullName.Contains('/', StringComparison.Ordinal));

        if (entry is null)
        {
            return null;
        }

        try
        {
            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            var metadata = doc.Root?.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "metadata");

            string? Get(string name) => metadata?
                .Elements()
                .FirstOrDefault(e => e.Name.LocalName == name)?
                .Value;

            return new NuspecMetadata(
                Id: Get("id"),
                Version: Get("version"),
                Title: Get("title") ?? Get("id"),
                TargetAbi: Get("magpieAbi") ?? Get("targetAbi"),
                Authors: NormalizeOrNull(Get("authors")),
                Description: NormalizeOrNull(Get("description")));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The extension service failed to read nuspec metadata from {NuspecEntry} " +
                "and will use fallback metadata",
                entry.FullName);
            return null;
        }
    }

    private static string? ChoosePreferredLibFolder(ZipArchive archive)
    {
        var libFolders = archive.Entries
            .Where(e => e.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase))
            .Select(e => { var parts = e.FullName.Split('/'); return parts.Length >= 2 ? parts[1] : null; })
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var preferred in PreferredTargetFrameworks)
        {
            if (libFolders.Contains(preferred, StringComparer.OrdinalIgnoreCase))
            {
                return preferred;
            }
        }

        return libFolders.FirstOrDefault();
    }

    private static string? ExtractLibFolder(
        ZipArchive archive,
        string? libFolder,
        string targetDir,
        IFileSystem fileSystem)
    {
        if (libFolder is null)
        {
            string? rootDll = null;
            foreach (var entry in archive.Entries)
            {
                if (IsDirectoryEntry(entry))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                if (entry.FullName.Contains('/', StringComparison.Ordinal))
                {
                    continue;
                }

                var path = ResolveSafeExtractionPath(targetDir, entry.Name, entry, fileSystem);
                ExtractEntry(entry, path, fileSystem);
                if (rootDll is null && entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    rootDll = path;
                }
            }

            return rootDll;
        }

        var prefix = $"lib/{libFolder}/";
        string? mainDll = null;
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsDirectoryEntry(entry))
            {
                continue;
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var relative = entry.FullName.Substring(prefix.Length);
            var path = ResolveSafeExtractionPath(targetDir, relative, entry, fileSystem);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                fileSystem.CreateDirectory(directory);
            }

            ExtractEntry(entry, path, fileSystem);

            if (mainDll is null && relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                mainDll = path;
            }
        }

        return mainDll;
    }

    private static void ExtractNuspec(ZipArchive archive, string targetDir, IFileSystem fileSystem)
    {
        var entry = archive.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) &&
            !e.FullName.Contains('/', StringComparison.Ordinal));
        if (entry is null)
        {
            return;
        }

        if (IsDirectoryEntry(entry) || string.IsNullOrEmpty(entry.Name))
        {
            return;
        }

        var path = ResolveSafeExtractionPath(targetDir, entry.Name, entry, fileSystem);
        ExtractEntry(entry, path, fileSystem);
    }

    private static void ExtractEntry(ZipArchiveEntry entry, string destinationPath, IFileSystem fileSystem)
    {
        using var input = entry.Open();
        using var output = fileSystem.CreateFile(destinationPath);
        input.CopyTo(output);
    }

    private static string ResolveSafeExtractionPath(
        string targetDir,
        string relative,
        ZipArchiveEntry entry,
        IFileSystem fileSystem)
    {
        var combined = Path.Combine(targetDir, relative);
        var resolvedTarget = NormalizeDirectory(targetDir, fileSystem);
        var resolvedCombined = fileSystem.GetFullPath(combined);
        if (fileSystem.PathsEqual(resolvedCombined, resolvedTarget) ||
            !fileSystem.IsPathWithin(resolvedTarget, resolvedCombined))
        {
            throw new InvalidOperationException($"The archive entry is outside the target: {entry.FullName}.");
        }

        return resolvedCombined;
    }

    private static bool IsDirectoryEntry(ZipArchiveEntry entry)
    {
        return entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
    }

    private static string SanitizeDirectoryName(string suggested)
    {
        if (string.IsNullOrWhiteSpace(suggested))
        {
            return $"extension-{Guid.NewGuid():N}";
        }

        var name = Path.GetFileNameWithoutExtension(suggested.Trim());
        return SanitizePathSegment(name);
    }

    private static string SanitizePathSegment(string suggested)
    {
        if (string.IsNullOrWhiteSpace(suggested))
        {
            return $"extension-{Guid.NewGuid():N}";
        }

        if (suggested.Trim().TrimEnd('.') is "" || suggested.Contains('/') || suggested.Contains('\\'))
        {
            throw new ArgumentException("The extension directory name is invalid.", nameof(suggested));
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string([.. suggested.Trim().Select(c => invalid.Contains(c) ? '-' : c)]);
        return string.IsNullOrWhiteSpace(sanitized)
            ? $"extension-{Guid.NewGuid():N}"
            : sanitized;
    }

    private static string NormalizeDirectory(string directory, IFileSystem fileSystem)
    {
        return Path.TrimEndingDirectorySeparator(fileSystem.GetFullPath(directory));
    }

    private static bool IsNewer(string candidate, string current)
    {
        if (Version.TryParse(StripPrerelease(candidate), out var parsedCandidate) &&
            Version.TryParse(StripPrerelease(current), out var parsedCurrent))
        {
            return parsedCandidate > parsedCurrent ||
                (parsedCandidate == parsedCurrent && !IsPrerelease(candidate) && IsPrerelease(current));
        }

        return string.CompareOrdinal(candidate, current) > 0;
    }

    private static bool HasChanges(ExtensionPatch patch) =>
        patch.NuGetId is not null ||
        patch.PackageRepository is not null ||
        patch.AutoUpgrade is not null ||
        patch.LoadOrder is not null ||
        patch.Enabled is not null ||
        patch.DisabledReason is not null;

    private static bool IsPrerelease(string version) =>
        version.Split('+', 2)[0].Contains('-', StringComparison.Ordinal);

    private static string StripPrerelease(string version)
    {
        var dash = version.IndexOf('-', StringComparison.Ordinal);
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        var cut = dash >= 0 && plus >= 0 ? Math.Min(dash, plus)
            : dash >= 0 ? dash
            : plus >= 0 ? plus
            : -1;
        return cut < 0 ? version : version.Substring(0, cut);
    }

    private static string? NormalizeOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string BuildSearchQuery(ExtensionPackageSource source, string? query)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query))
        {
            parts.Add(query.Trim());
        }

        if (source.SearchFilterKind == ExtensionPackageSourceSearchFilterKind.Tag)
        {
            foreach (var tag in SplitFilterValues(source.SearchFilterValue ?? DefaultTagFilter))
            {
                parts.Add(tag.StartsWith("tags:", StringComparison.OrdinalIgnoreCase) ? tag : $"tags:{tag}");
            }
        }
        else if (source.SearchFilterKind == ExtensionPackageSourceSearchFilterKind.Prefix &&
            !string.IsNullOrWhiteSpace(source.SearchFilterValue))
        {
            parts.Add(source.SearchFilterValue.Trim());
        }

        return string.Join(" ", parts);
    }

    private static bool MatchesSourceFilter(ExtensionPackageSource source, NuGetPackage package)
    {
        if (source.SearchFilterKind != ExtensionPackageSourceSearchFilterKind.Prefix ||
            string.IsNullOrWhiteSpace(source.SearchFilterValue))
        {
            return true;
        }

        return package.Id?.StartsWith(source.SearchFilterValue.Trim(), StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string AppendSearchParameters(string searchUrl, string query)
    {
        var separator = searchUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{searchUrl}{separator}q={Uri.EscapeDataString(query)}&prerelease=false&semVerLevel=2.0.0";
    }

    private static IEnumerable<string> SplitFilterValues(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool LooksLikeSearchEndpoint(string sourceUrl) =>
        sourceUrl.Contains("/query", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeFlatContainerEndpoint(string sourceUrl) =>
        sourceUrl.Contains("v3-flatcontainer", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadSourceUri(string? value, [NotNullWhen(true)] out Uri? sourceUri)
    {
        sourceUri = Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
            string.IsNullOrEmpty(uri.UserInfo)
            ? uri
            : null;
        return sourceUri is not null;
    }

    private static void ValidateSourceUri(string? value, string parameterName)
    {
        if (!TryReadSourceUri(value, out _))
        {
            throw new ArgumentException("The package source must be an absolute HTTPS URL.", parameterName);
        }
    }

    public async Task<IReadOnlyList<ExtensionListItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        var extensions = await extensionRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        var updateInfo = await GetUpdateAvailabilityAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. extensions.Select(extension => new ExtensionListItem
            {
                Extension = extension,
                UpdateInfo = updateInfo.TryGetValue(extension.Id, out var info) ? info : null
            })
        ];
    }

    public async Task<Extension?> GetAsync(Guid extensionId, CancellationToken cancellationToken = default)
    {
        return await extensionRepository.GetByIdAsync(extensionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<NuGetSearchResult>> SearchAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var results = new List<NuGetSearchResult>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in await GetEnabledSourcesAsync(cancellationToken).ConfigureAwait(false))
            {
                await AddSearchResultsAsync(source, query, results, seen, cancellationToken).ConfigureAwait(false);
            }

            return results;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The extension service failed to search NuGet packages with query {Query}", query);
            return [];
        }
    }

    public async Task<ExtensionInstallResult> InstallFromSourceAsync(
        string packageId,
        string? version,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new ArgumentException("A package ID is required.", nameof(packageId));
        }

        var stream = await DownloadPackageAsync(packageId, version, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"NuGet package {packageId} could not be downloaded.");

        await using var streamHandle = stream.ConfigureAwait(false);
        var extension = await InstallFromStreamInternalAsync(
            stream,
            suggestedName: packageId,
            actingUserId,
            cancellationToken,
            packageId: packageId,
            managed: true).ConfigureAwait(false);

        return new ExtensionInstallResult { Extension = extension, RestartRequired = true };
    }

    public async Task<ExtensionInstallResult> UploadAsync(
        Stream package,
        string fileName,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        Extension extension;
        try
        {
            extension = await InstallFromStreamInternalAsync(package, fileName, actingUserId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            logger.LogError(exception, "The uploaded package {FileName} is not a valid archive", fileName);
            throw new MagpieException("The uploaded file is not a valid extension package.");
        }

        return new ExtensionInstallResult { Extension = extension, RestartRequired = true };
    }

    public async Task<ExtensionInstallResult?> UpgradeToLatestAsync(
        Guid extensionId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        return await UpgradeAsync(extensionId, version: null, actingUserId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ExtensionInstallResult?> UpgradeToVersionAsync(
        Guid extensionId,
        string version,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        return await UpgradeAsync(extensionId, version, actingUserId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Extension?> EnableAsync(
        Guid extensionId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var existing = await extensionRepository.GetByIdAsync(extensionId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        var updated = await extensionRepository.PatchAsync(
            new ExtensionPatch
            {
                Id = extensionId,
                Enabled = new() { Value = true },
                DisabledReason = new() { Value = null }
            },
            actingUserId,
            cancellationToken).ConfigureAwait(false);
        if (!updated)
        {
            return null;
        }

        await PublishActivationChangedAsync(cancellationToken).ConfigureAwait(false);
        await eventBus.PublishAsync(
            ExtensionEventBuilder.BuildExtensionEnabledEvent(extensionId),
            cancellationToken).ConfigureAwait(false);
        return existing with
        {
            Enabled = true,
            DisabledReason = null,
            ModifiedAt = timeProvider.GetUtcNow().UtcDateTime,
            ModifiedByUserId = actingUserId
        };
    }

    public async Task<Extension?> DisableAsync(
        Guid extensionId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var existing = await extensionRepository.GetByIdAsync(extensionId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        await lifecycleManager.InvokeDisableAsync(existing, cancellationToken).ConfigureAwait(false);

        var updated = await extensionRepository.PatchAsync(
            new ExtensionPatch { Id = extensionId, Enabled = new() { Value = false } },
            actingUserId,
            cancellationToken).ConfigureAwait(false);
        if (!updated)
        {
            return null;
        }

        await PublishActivationChangedAsync(cancellationToken).ConfigureAwait(false);
        await eventBus.PublishAsync(
            ExtensionEventBuilder.BuildExtensionDisabledEvent(extensionId, ManualDisableReason),
            cancellationToken)
            .ConfigureAwait(false);
        return existing with
        {
            Enabled = false,
            ModifiedAt = timeProvider.GetUtcNow().UtcDateTime,
            ModifiedByUserId = actingUserId
        };
    }

    public async Task<bool> PatchAsync(
        Guid extensionId,
        ExtensionPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var effective = patch with { Id = extensionId };
        if (effective.Enabled is { Value: true })
        {
            effective = effective with { DisabledReason = patch.DisabledReason ?? new() { Value = null } };
        }

        if (!HasChanges(effective))
        {
            return await extensionRepository.GetByIdAsync(extensionId, cancellationToken)
                .ConfigureAwait(false) is not null;
        }

        if (effective.Enabled is { Value: false })
        {
            var existing = await extensionRepository.GetByIdAsync(extensionId, cancellationToken)
                .ConfigureAwait(false);
            if (existing is null)
            {
                return false;
            }

            await lifecycleManager.InvokeDisableAsync(existing, cancellationToken).ConfigureAwait(false);
        }

        var patched = await extensionRepository.PatchAsync(effective, actingUserId, cancellationToken)
            .ConfigureAwait(false);
        if (!patched)
        {
            return false;
        }

        if (effective.Enabled is not null || effective.LoadOrder is not null)
        {
            await PublishActivationChangedAsync(cancellationToken).ConfigureAwait(false);
        }

        if (effective.Enabled is { } enabled)
        {
            await eventBus.PublishAsync(
                enabled.Value
                    ? ExtensionEventBuilder.BuildExtensionEnabledEvent(extensionId)
                    : ExtensionEventBuilder.BuildExtensionDisabledEvent(extensionId, ManualDisableReason),
                cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    public async Task<bool> UninstallAsync(
        Guid extensionId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var existing = await extensionRepository.GetByIdAsync(extensionId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return false;
        }

        await lifecycleManager.InvokeUninstallAsync(existing, cancellationToken).ConfigureAwait(false);
        var deleted = await extensionRepository.DeleteAsync(extensionId, actingUserId, cancellationToken)
            .ConfigureAwait(false);
        if (!deleted)
        {
            return false;
        }

        RemoveExtensionDirectory(existing);
        await PublishActivationChangedAsync(cancellationToken).ConfigureAwait(false);
        await eventBus.PublishAsync(
            ExtensionEventBuilder.BuildExtensionUninstalledEvent(extensionId, existing.AssemblyName, existing.FilePath),
            cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<ExtensionPackageSource>> ListPackageSourcesAsync(
        CancellationToken cancellationToken = default)
    {
        return [.. await packageSourceRepository.ListAllAsync(cancellationToken).ConfigureAwait(false)];
    }

    public async Task<ExtensionPackageSource?> GetPackageSourceAsync(
        Guid packageSourceId,
        CancellationToken cancellationToken = default)
    {
        return await packageSourceRepository.GetByIdAsync(packageSourceId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ExtensionPackageSource> CreatePackageSourceAsync(
        ExtensionPackageSourceCreate create,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        ValidateSourceUri(create.SourceUrl, nameof(create));
        return await packageSourceRepository.CreateAsync(
            new ExtensionPackageSource
            {
                Name = create.Name,
                SourceUrl = create.SourceUrl?.Trim() ?? string.Empty,
                Enabled = create.Enabled,
                SearchFilterKind = create.SearchFilterKind,
                SearchFilterValue = create.SearchFilterValue,
                CredentialKind = create.CredentialKind,
                Username = create.Username,
                EncryptedSecret = create.Secret,
                SecretReference = create.SecretReference
            },
            actingUserId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> PatchPackageSourceAsync(
        Guid packageSourceId,
        ExtensionPackageSourcePatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        if (patch.SourceUrl is { } sourceUrl)
        {
            ValidateSourceUri(sourceUrl.Value, nameof(patch));
        }

        return await packageSourceRepository
            .PatchAsync(patch with { Id = packageSourceId }, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeletePackageSourceAsync(
        Guid packageSourceId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        return await packageSourceRepository.DeleteAsync(packageSourceId, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ExtensionInstallResult?> UpgradeAsync(
        Guid extensionId,
        string? version,
        Guid? actingUserId,
        CancellationToken cancellationToken)
    {
        var existing = await extensionRepository.GetByIdAsync(extensionId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        if (!existing.Managed || string.IsNullOrWhiteSpace(existing.PackageId))
        {
            throw new InvalidOperationException(
                $"Extension {existing.Name} is not a NuGet-managed extension and cannot be updated.");
        }

        if (!TryResolveExtensionRootDirectory(existing, out var targetDir))
        {
            throw new InvalidOperationException(
                $"Extension {existing.Name} does not have a safe staging directory under the extensions root.");
        }

        var stream = await DownloadPackageAsync(existing.PackageId, version, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"NuGet package {existing.PackageId} could not be downloaded.");

        try
        {
            Directory.CreateDirectory(hostEnvironment.ExtensionsRoot);

            var tempDir = Path.Combine(hostEnvironment.ExtensionsRoot, $".update-{existing.Id:N}-{Guid.NewGuid():N}");
            var backupDir = Path.Combine(hostEnvironment.ExtensionsRoot, $".backup-{existing.Id:N}-{Guid.NewGuid():N}");

            Extension staged;
            try
            {
                Directory.CreateDirectory(tempDir);
                staged = ExtractPackage(
                    stream,
                    tempDir,
                    directoryName: Path.GetFileName(targetDir),
                    fallbackPackageId: existing.PackageId,
                    managed: true,
                    fileSystem: fileSystem,
                    timeProvider: timeProvider,
                    logger: logger);
            }
            catch
            {
                if (Directory.Exists(tempDir))
                {
                    TryDeleteDirectory(tempDir);
                }

                throw;
            }

            var updated = BuildUpdatedExtension(existing, staged, tempDir, targetDir, fileSystem);
            var movedExisting = false;
            var movedReplacement = false;

            try
            {
                if (Directory.Exists(targetDir))
                {
                    Directory.Move(targetDir, backupDir);
                    movedExisting = true;
                }

                Directory.Move(tempDir, targetDir);
                movedReplacement = true;

                var saved = await extensionRepository.UpdateAsync(updated, actingUserId, cancellationToken)
                    .ConfigureAwait(false);
                if (!saved)
                {
                    throw new InvalidOperationException(
                        $"Extension {existing.Name} no longer exists and could not be updated.");
                }

                if (movedExisting && Directory.Exists(backupDir))
                {
                    TryDeleteDirectory(backupDir);
                }

                logger.LogInformation(
                    "The extension service updated extension {ExtensionName} to v{ExtensionVersion} in " +
                    "{TargetDirectory} and needs a restart",
                    updated.Name,
                    updated.Version,
                    targetDir);
                await PublishActivationChangedAsync(cancellationToken).ConfigureAwait(false);
                await eventBus.PublishAsync(
                    ExtensionEventBuilder.BuildExtensionUpgradedEvent(existing.Id, existing.Version, updated.Version),
                    cancellationToken)
                    .ConfigureAwait(false);
                return new ExtensionInstallResult { Extension = updated, RestartRequired = true };
            }
            catch
            {
                RestoreUpdatedExtensionDirectory(targetDir, tempDir, backupDir, movedExisting, movedReplacement);
                throw;
            }
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyDictionary<Guid, ExtensionUpdateInfo>> GetUpdateAvailabilityAsync(
        CancellationToken cancellationToken)
    {
        if (_updateInfoCache.TryGet<IReadOnlyDictionary<Guid, ExtensionUpdateInfo>>(
                UpdateInfoCacheKey,
                out var cached) && cached is not null)
        {
            DataTelemetry.CacheHit.Add(1, new KeyValuePair<string, object?>("cache", "update_check"));
            return cached;
        }

        DataTelemetry.CacheMiss.Add(1, new KeyValuePair<string, object?>("cache", "update_check"));

        try
        {
            var extensions = await extensionRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
            var updates = new List<ExtensionUpdateInfo>();

            foreach (var extension in extensions)
            {
                if (!extension.Managed || string.IsNullOrWhiteSpace(extension.PackageId))
                {
                    updates.Add(new ExtensionUpdateInfo(
                        extension.Id,
                        extension.PackageId,
                        extension.Version,
                        LatestVersion: null,
                        HasUpdate: false));
                    continue;
                }

                var versions = await GetVersionsAsync(extension.PackageId, cancellationToken)
                    .ConfigureAwait(false);
                var latest = versions.LastOrDefault(candidate => !IsPrerelease(candidate));
                var hasUpdate = !string.IsNullOrWhiteSpace(latest) && IsNewer(latest!, extension.Version);

                updates.Add(new ExtensionUpdateInfo(
                    extension.Id,
                    extension.PackageId,
                    extension.Version,
                    latest,
                    hasUpdate));
            }

            var map = updates.ToDictionary(update => update.ExtensionId);

            _updateInfoCache.Set(UpdateInfoCacheKey, map);

            return map;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The extension service failed to get update information");
            return new Dictionary<Guid, ExtensionUpdateInfo>();
        }
    }

    private async Task<Extension> InstallFromStreamInternalAsync(
        Stream packageStream,
        string suggestedName,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken,
        string? packageId = null,
        bool managed = false)
    {
        if (!managed && hostOptions.Cluster.Mode == ExtensionClusterMode.MultiNodeLocalExtensions)
        {
            throw new MagpieException(
                "Local extension uploads are unavailable in multi-node extension mode; install from a " +
                "configured NuGet source instead.");
        }

        var buffer = new MemoryStream();
        await packageStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;

        var directoryName = packageId is null
            ? SanitizeDirectoryName(suggestedName)
            : SanitizePathSegment(packageId);
        var extension = await StageAndRegisterAsync(
            buffer,
            directoryName,
            fallbackPackageId: packageId,
            managed,
            modifiedByUserId,
            cancellationToken).ConfigureAwait(false);
        await PublishActivationChangedAsync(cancellationToken).ConfigureAwait(false);
        await eventBus.PublishAsync(
            ExtensionEventBuilder.BuildExtensionInstalledEvent(extension.Id, extension.Version),
            cancellationToken)
            .ConfigureAwait(false);
        return extension;
    }

    private async Task<Extension> StageAndRegisterAsync(
        Stream packageStream,
        string directoryName,
        string? fallbackPackageId,
        bool managed,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(hostEnvironment.ExtensionsRoot);
        var targetDir = fileSystem.GetFullPath(Path.Combine(hostEnvironment.ExtensionsRoot, directoryName));
        if (!fileSystem.IsPathWithin(hostEnvironment.ExtensionsRoot, targetDir) ||
            fileSystem.PathsEqual(hostEnvironment.ExtensionsRoot, targetDir) ||
            !fileSystem.PathsEqual(Path.GetDirectoryName(targetDir)!, hostEnvironment.ExtensionsRoot))
        {
            throw new ArgumentException(
                "The extension directory is outside the extensions root.", nameof(directoryName));
        }

        var tempDir = Path.Combine(hostEnvironment.ExtensionsRoot, $".install-{Guid.NewGuid():N}");
        var backupDir = Path.Combine(hostEnvironment.ExtensionsRoot, $".backup-{Guid.NewGuid():N}");
        var movedExisting = false;
        var movedReplacement = false;

        try
        {
            Directory.CreateDirectory(tempDir);
            var staged = ExtractPackage(
                packageStream, tempDir, directoryName, fallbackPackageId, managed, fileSystem, timeProvider, logger);
            var entity = BuildStagedExtensionForTarget(staged, tempDir, targetDir, fileSystem);

            var existing = (await extensionRepository.QueryAsync(
                new ExtensionQuery { PackageId = entity.PackageId },
                new() { PageNumber = 1, PageSize = 1 },
                null,
                cancellationToken).ConfigureAwait(false)).Items.SingleOrDefault();

            if (Directory.Exists(targetDir))
            {
                Directory.Move(targetDir, backupDir);
                movedExisting = true;
            }

            Directory.Move(tempDir, targetDir);
            movedReplacement = true;

            var registered = entity;
            if (existing is null)
            {
                registered = await extensionRepository.CreateAsync(entity, modifiedByUserId, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                registered = BuildRegisteredExtension(existing, entity);
                var saved = await extensionRepository.UpdateAsync(registered, modifiedByUserId, cancellationToken)
                    .ConfigureAwait(false);
                if (!saved)
                {
                    throw new InvalidOperationException(
                        $"Extension {existing.Name} no longer exists and could not be updated.");
                }

                RemovePreviousExtensionDirectory(existing, targetDir);
            }

            if (movedExisting && Directory.Exists(backupDir))
            {
                TryDeleteDirectory(backupDir);
            }

            logger.LogInformation(
                "The extension service installed extension {ExtensionName} v{ExtensionVersion} in " +
                "{TargetDirectory} and needs a restart",
                registered.Name,
                registered.Version,
                targetDir);
            return registered;
        }
        catch
        {
            RestoreUpdatedExtensionDirectory(targetDir, tempDir, backupDir, movedExisting, movedReplacement);
            throw;
        }
    }

    private void RemovePreviousExtensionDirectory(Extension existing, string targetDir)
    {
        if (!TryResolveExtensionRootDirectory(existing, out var previousDir) || !Directory.Exists(previousDir))
        {
            return;
        }

        if (fileSystem.PathsEqual(
            NormalizeDirectory(previousDir, fileSystem),
            NormalizeDirectory(targetDir, fileSystem)))
        {
            return;
        }

        TryDeleteDirectory(previousDir);
    }

    private void RemoveExtensionDirectory(Extension extension)
    {
        if (!TryResolveExtensionRootDirectory(extension, out var directory) || !Directory.Exists(directory))
        {
            return;
        }

        TryDeleteDirectory(directory);
    }

    private bool TryResolveExtensionRootDirectory(Extension extension, out string directory)
    {
        directory = string.Empty;
        if (string.IsNullOrWhiteSpace(extension.FilePath))
        {
            return false;
        }

        var fullRoot = NormalizeDirectory(hostEnvironment.ExtensionsRoot, fileSystem);
        var fullFilePath = fileSystem.GetFullPath(extension.FilePath);
        if (!fileSystem.IsPathWithin(fullRoot, fullFilePath) || fileSystem.PathsEqual(fullRoot, fullFilePath))
        {
            logger.LogWarning(
                "The extension service rejected path {FilePath} outside root {Root}",
                fullFilePath,
                fullRoot);
            return false;
        }

        var fullFileDirectory = Path.GetDirectoryName(fullFilePath);
        if (string.IsNullOrWhiteSpace(fullFileDirectory))
        {
            return false;
        }

        var relativeDirectory = fileSystem.GetRelativePath(fullRoot, fullFileDirectory);
        if (string.IsNullOrWhiteSpace(relativeDirectory) ||
            fileSystem.PathsEqual(fullRoot, fullFileDirectory))
        {
            logger.LogWarning(
                "The extension service rejected directory {Directory} outside a staging subdirectory of " +
                "{Root}",
                fullFileDirectory,
                fullRoot);
            return false;
        }

        var firstSegment = relativeDirectory
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstSegment))
        {
            return false;
        }

        directory = Path.Combine(fullRoot, firstSegment);
        return true;
    }

    private void RestoreUpdatedExtensionDirectory(
        string targetDir,
        string tempDir,
        string backupDir,
        bool movedExisting,
        bool movedReplacement)
    {
        try
        {
            if (movedReplacement && Directory.Exists(targetDir))
            {
                TryDeleteDirectory(targetDir);
            }

            if (movedExisting && Directory.Exists(backupDir) && !Directory.Exists(targetDir))
            {
                Directory.Move(backupDir, targetDir);
            }
            else if (movedExisting && Directory.Exists(backupDir))
            {
                logger.LogWarning(
                    "The extension service did not restore backup {BackupDirectory} because target " +
                    "{TargetDirectory} already exists",
                    backupDir,
                    targetDir);
            }

            if (Directory.Exists(tempDir))
            {
                TryDeleteDirectory(tempDir);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The extension service failed to restore extension directory {TargetDirectory}",
                targetDir);
        }
    }

    private void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The extension service failed to delete extension directory {Directory}",
                directory);
        }
    }

    private async Task<Stream?> DownloadPackageAsync(
        string packageId,
        string? version,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return null;
        }

        var resolvedVersion = version;
        if (string.IsNullOrWhiteSpace(resolvedVersion))
        {
            var versions = await GetVersionsAsync(packageId, cancellationToken).ConfigureAwait(false);
            resolvedVersion = versions.LastOrDefault(candidate => !IsPrerelease(candidate));
            if (string.IsNullOrWhiteSpace(resolvedVersion))
            {
                if (versions.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"NuGet package {packageId} publishes prerelease versions only; name a version to " +
                        "install it.");
                }

                return null;
            }
        }

        if (packageId is "." or "..")
        {
            throw new ArgumentException("The package ID is invalid.", nameof(packageId));
        }

        var lowerId = Uri.EscapeDataString(packageId.ToLowerInvariant());
        if (resolvedVersion is "." or "..")
        {
            throw new ArgumentException("The package version is invalid.", nameof(version));
        }

        var lowerVersion = Uri.EscapeDataString(resolvedVersion!.ToLowerInvariant());

        try
        {
            var sources = await GetEnabledSourcesAsync(cancellationToken).ConfigureAwait(false);
            foreach (var source in sources)
            {
                try
                {
                    var endpoints = await ResolveEndpointsAsync(source, cancellationToken).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(endpoints.FlatContainerUrl))
                    {
                        continue;
                    }

                    var requestUrl =
                        $"{endpoints.FlatContainerUrl.TrimEnd('/')}/{lowerId}/{lowerVersion}/" +
                        $"{lowerId}.{lowerVersion}.nupkg";
                    using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                    ApplyCredentials(request, source);

                    using var response = await httpClientFactory.CreateClient(HttpClientName)
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);

                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        continue;
                    }

                    response.EnsureSuccessStatusCode();

                    var buffer = new MemoryStream();
                    try
                    {
                        await response.Content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                        buffer.Position = 0;
                        await VerifyPackageIntegrityAsync(
                            buffer, endpoints.RegistrationUrl, packageId, resolvedVersion, source, cancellationToken)
                            .ConfigureAwait(false);
                        buffer.Position = 0;
                        return buffer;
                    }
                    catch
                    {
                        await buffer.DisposeAsync().ConfigureAwait(false);
                        throw;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (CryptographicException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "The extension service failed to download NuGet package {PackageId} {Version} " +
                        "from {PackageSourceName}",
                        packageId,
                        resolvedVersion,
                        source.Name);
                }
            }

            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CryptographicException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The extension service failed to download NuGet package {PackageId} {Version}",
                packageId,
                resolvedVersion);
            return null;
        }
    }

    private async Task VerifyPackageIntegrityAsync(
        Stream package,
        string? registrationUrl,
        string packageId,
        string version,
        ExtensionPackageSource source,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(registrationUrl))
        {
            if (!LooksLikeSearchEndpoint(source.SourceUrl) && !LooksLikeFlatContainerEndpoint(source.SourceUrl))
            {
                throw new InvalidDataException("The package source has no integrity metadata.");
            }

            logger.LogWarning(
                "NuGet source {PackageSourceName} installed package {PackageId} {Version} without integrity " +
                "verification because it publishes no package hashes",
                source.Name,
                packageId,
                version);
            return;
        }

        var indexUrl =
            $"{registrationUrl.TrimEnd('/')}/{Uri.EscapeDataString(packageId.ToLowerInvariant())}/index.json";
        var index = await GetFromJsonAsync<JsonElement>(indexUrl, source, cancellationToken).ConfigureAwait(false);
        foreach (var page in index.GetProperty("items").EnumerateArray())
        {
            var entries = page.TryGetProperty("items", out var inlineEntries)
                ? inlineEntries
                : (await GetFromJsonAsync<JsonElement>(
                    page.GetProperty("@id").GetString()!, source, cancellationToken).ConfigureAwait(false))
                    .GetProperty("items");
            foreach (var entry in entries.EnumerateArray())
            {
                var catalog = entry.GetProperty("catalogEntry");
                if (!string.Equals(
                    catalog.GetProperty("version").GetString(), version, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var metadata = await GetFromJsonAsync<JsonElement>(
                    catalog.GetProperty("@id").GetString()!, source, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(
                    metadata.GetProperty("id").GetString(), packageId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        metadata.GetProperty("version").GetString(), version, StringComparison.OrdinalIgnoreCase) ||
                    metadata.GetProperty("packageHashAlgorithm").GetString() != "SHA512")
                {
                    throw new InvalidDataException("The package integrity metadata is invalid.");
                }

                var expected = Convert.FromBase64String(metadata.GetProperty("packageHash").GetString()!);
                var actual = await SHA512.HashDataAsync(package, cancellationToken).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                {
                    throw new CryptographicException("The package hash does not match the source metadata.");
                }

                return;
            }
        }

        throw new InvalidDataException("The package source has no integrity metadata for this version.");
    }

    private async Task<IReadOnlyList<string>> GetVersionsAsync(string packageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return [];
        }

        if (packageId is "." or "..")
        {
            throw new ArgumentException("The package ID is invalid.", nameof(packageId));
        }

        var lowerId = Uri.EscapeDataString(packageId.ToLowerInvariant());

        try
        {
            var sources = await GetEnabledSourcesAsync(cancellationToken).ConfigureAwait(false);
            foreach (var source in sources)
            {
                try
                {
                    var endpoints = await ResolveEndpointsAsync(source, cancellationToken).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(endpoints.FlatContainerUrl))
                    {
                        continue;
                    }

                    var requestUrl = $"{endpoints.FlatContainerUrl.TrimEnd('/')}/{lowerId}/index.json";
                    var response = await GetFromJsonAsync<NuGetVersionsResponse>(requestUrl, source, cancellationToken)
                        .ConfigureAwait(false);

                    if (response?.Versions is { Count: > 0 } versions)
                    {
                        return versions;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "The extension service failed to list the NuGet versions of {PackageId} " +
                        "from {PackageSourceName}",
                        packageId,
                        source.Name);
                }
            }

            return [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The extension service failed to list the NuGet versions of {PackageId}",
                packageId);
            return [];
        }
    }

    private async Task AddSearchResultsAsync(
        ExtensionPackageSource source,
        string? query,
        ICollection<NuGetSearchResult> results,
        ISet<string> seen,
        CancellationToken cancellationToken)
    {
        try
        {
            var endpoints = await ResolveEndpointsAsync(source, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(endpoints.SearchUrl))
            {
                return;
            }

            var searchQuery = BuildSearchQuery(source, query);
            var requestUrl = AppendSearchParameters(endpoints.SearchUrl, searchQuery);
            var response = await GetFromJsonAsync<NuGetSearchResponse>(requestUrl, source, cancellationToken)
                .ConfigureAwait(false);

            if (response?.Data is null)
            {
                return;
            }

            foreach (var package in response.Data)
            {
                if (string.IsNullOrWhiteSpace(package.Id) ||
                    string.IsNullOrWhiteSpace(package.Version) ||
                    !MatchesSourceFilter(source, package))
                {
                    continue;
                }

                if (!seen.Add($"{package.Id}|{package.Version}"))
                {
                    continue;
                }

                results.Add(new NuGetSearchResult
                {
                    Id = package.Id,
                    Version = package.Version,
                    Title = package.Title,
                    Description = package.Description,
                    Authors = package.Authors ?? [],
                    IconUrl = package.IconUrl,
                    ProjectUrl = package.ProjectUrl,
                    TotalDownloads = package.TotalDownloads,
                    Tags = package.Tags ?? []
                });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The extension service failed to search NuGet source {PackageSourceName}",
                source.Name);
        }
    }

    private async Task<IReadOnlyList<ExtensionPackageSource>> GetEnabledSourcesAsync(
        CancellationToken cancellationToken)
    {
        var sources = await packageSourceRepository.QueryAllAsync(
            new ExtensionPackageSourceQuery { Enabled = true },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return sources;
    }

    private async Task<NuGetSourceEndpoints> ResolveEndpointsAsync(
        ExtensionPackageSource source,
        CancellationToken cancellationToken)
    {
        if (LooksLikeSearchEndpoint(source.SourceUrl))
        {
            return new(SearchUrl: source.SourceUrl, FlatContainerUrl: null);
        }

        if (LooksLikeFlatContainerEndpoint(source.SourceUrl))
        {
            return new(SearchUrl: null, FlatContainerUrl: source.SourceUrl);
        }

        try
        {
            var index = await GetFromJsonAsync<NuGetServiceIndex>(source.SourceUrl, source, cancellationToken)
                .ConfigureAwait(false);

            var searchUrl = index?.Resources?
                .FirstOrDefault(r => r.Type?.Contains("SearchQueryService", StringComparison.OrdinalIgnoreCase) == true)
                ?.Id;
            var flatContainerUrl = index?.Resources?
                .FirstOrDefault(r =>
                    r.Type?.StartsWith("PackageBaseAddress", StringComparison.OrdinalIgnoreCase) == true)
                ?.Id;

            var registrationUrl = SupportedRegistrationTypes
                .Select(type => index?.Resources?.FirstOrDefault(resource =>
                    string.Equals(resource.Type, type, StringComparison.OrdinalIgnoreCase))?.Id)
                .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
            return new(SearchUrl: searchUrl, FlatContainerUrl: flatContainerUrl, RegistrationUrl: registrationUrl);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The extension service failed to resolve NuGet source {PackageSourceName}",
                source.Name);
            return new(SearchUrl: null, FlatContainerUrl: null);
        }
    }

    private async Task<T?> GetFromJsonAsync<T>(
        string requestUrl,
        ExtensionPackageSource source,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        ApplyCredentials(request, source);

        using var response = await httpClientFactory.CreateClient(HttpClientName)
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
    }

    private void ApplyCredentials(HttpRequestMessage request, ExtensionPackageSource source)
    {
        if (source.CredentialKind == ExtensionPackageSourceCredentialKind.None)
        {
            return;
        }

        if (!TryReadSourceUri(source.SourceUrl, out var sourceUri))
        {
            logger.LogWarning(
                "NuGet source {PackageSourceName} has no absolute HTTPS URL; the extension service calls " +
                "it without credentials",
                source.Name);
            return;
        }

        if (!TryReadSourceUri(request.RequestUri?.AbsoluteUri, out var requestUri) ||
            !string.Equals(sourceUri.IdnHost, requestUri.IdnHost, StringComparison.OrdinalIgnoreCase) ||
            sourceUri.Port != requestUri.Port)
        {
            return;
        }

        string? secret;
        if (source.CredentialKind == ExtensionPackageSourceCredentialKind.Basic)
        {
            if (string.IsNullOrWhiteSpace(source.EncryptedSecret))
            {
                return;
            }

            secret = source.EncryptedSecret;
        }
        else if (source.CredentialKind == ExtensionPackageSourceCredentialKind.SecretReference)
        {
            var secretReference = source.SecretReference?.Trim();
            if (string.IsNullOrWhiteSpace(secretReference))
            {
                logger.LogWarning(
                    "NuGet source {PackageSourceName} uses secret credentials without a reference",
                    source.Name);
                return;
            }

            secret = Environment.GetEnvironmentVariable(secretReference);
            if (string.IsNullOrWhiteSpace(secret))
            {
                logger.LogWarning(
                    "NuGet source {PackageSourceName} reference {SecretReference} has no value; the " +
                    "extension service calls it without credentials",
                    source.Name,
                    secretReference);
                return;
            }
        }
        else
        {
            return;
        }

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{source.Username ?? string.Empty}:{secret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    private async Task PublishActivationChangedAsync(CancellationToken cancellationToken)
    {
        var generation = await activationGenerationRepository.BumpAsync(cancellationToken).ConfigureAwait(false);
        await eventBus
            .PublishAsync(ExtensionEventBuilder.BuildExtensionActivationChangedEvent(generation), cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed record NuGetSourceEndpoints(
        string? SearchUrl, string? FlatContainerUrl, string? RegistrationUrl = null);

    private sealed record NuGetServiceIndex
    {
        [JsonPropertyName("resources")]
        public IReadOnlyList<NuGetServiceResource>? Resources { get; init; }
    }

    private sealed record NuGetServiceResource
    {
        [JsonPropertyName("@id")]
        public string? Id { get; init; }

        [JsonPropertyName("@type")]
        public string? Type { get; init; }
    }

    private sealed record NuGetSearchResponse
    {
        [JsonPropertyName("data")]
        public IReadOnlyList<NuGetPackage>? Data { get; init; }
    }

    private sealed record NuGetPackage
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("version")]
        public string? Version { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("authors")]
        public IReadOnlyList<string>? Authors { get; init; }

        [JsonPropertyName("iconUrl")]
        public string? IconUrl { get; init; }

        [JsonPropertyName("projectUrl")]
        public string? ProjectUrl { get; init; }

        [JsonPropertyName("totalDownloads")]
        public long TotalDownloads { get; init; }

        [JsonPropertyName("tags")]
        public IReadOnlyList<string>? Tags { get; init; }
    }

    private sealed record NuGetVersionsResponse
    {
        [JsonPropertyName("versions")]
        public IReadOnlyList<string>? Versions { get; init; }
    }

    private sealed record NuspecMetadata(
        string? Id,
        string? Version,
        string? Title,
        string? TargetAbi,
        string? Authors,
        string? Description);
}
