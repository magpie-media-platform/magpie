using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Models.Data;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Extensions;

/// <summary>
/// Default <see cref="IExtensionInstaller"/> implementation.
/// </summary>
/// <remarks>
/// <para>
/// Stages each extension under <c>{ExtensionsRoot}/{packageId}/</c>. When installing from a
/// .nupkg the file is opened as a zip archive and the contents of the preferred
/// <c>lib/&lt;tfm&gt;</c> folder are extracted into the target directory along with the
/// .nuspec so future calls can read the metadata back.
/// </para>
/// <para>
/// Updates and uninstalls touch only on-disk staging and the database row; any assembly
/// that is currently loaded by the host stays loaded until the next restart. The
/// <c>.installed.json</c> sentinel that drives <see cref="IExtensionLifecycle.OnExtensionInstallAsync"/>
/// is also rewritten so that re-installing the same package id triggers the install hook
/// again on the next startup.
/// </para>
/// </remarks>
public sealed class ExtensionInstaller(
    IExtensionRepository extensionRepository,
    INuGetService nuGetService,
    ExtensionInstallerOptions options,
    ILogger<ExtensionInstaller> logger) : IExtensionInstaller
{
    /// <summary>File name of the install-tracking record under the extensions root; must match the constant used by the host's lifecycle dispatcher.</summary>
    public const string InstalledRecordFileName = ".installed.json";

    private static readonly string[] PreferredTargetFrameworks =
    [
        "netstandard2.1",
        "netstandard2.0",
        "net10.0",
        "net9.0",
        "net8.0"
    ];

    /// <inheritdoc />
    public async Task<Extension> InstallFromNuGetAsync(
        string packageId,
        string? version,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new ArgumentException("Package id is required.", nameof(packageId));
        }

        var stream = await nuGetService.DownloadPackageAsync(packageId, version, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"NuGet package '{packageId}' could not be downloaded.");

        try
        {
            var directoryName = packageId;
            var extension = await StageAndRegisterAsync(
                stream,
                directoryName,
                fallbackPackageId: packageId,
                managed: true,
                modifiedByUserId,
                cancellationToken).ConfigureAwait(false);
            return extension;
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<Extension> InstallFromStreamAsync(
        Stream packageStream,
        string suggestedName,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageStream);

        // Buffer to a seekable stream so the zip archive can be opened.
        var buffer = new MemoryStream();
        await packageStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;

        var directoryName = SanitizeDirectoryName(suggestedName);
        var extension = await StageAndRegisterAsync(
            buffer,
            directoryName,
            fallbackPackageId: null,
            managed: false,
            modifiedByUserId,
            cancellationToken).ConfigureAwait(false);
        return extension;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExtensionUpdateInfo>> CheckUpdatesAsync(CancellationToken cancellationToken)
    {
        var extensions = await extensionRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<ExtensionUpdateInfo>();

        foreach (var extension in extensions)
        {
            if (!extension.Managed || string.IsNullOrWhiteSpace(extension.NuGetId))
            {
                results.Add(new ExtensionUpdateInfo(
                    extension.Id,
                    extension.NuGetId,
                    extension.Version,
                    LatestVersion: null,
                    HasUpdate: false));
                continue;
            }

            var versions = await nuGetService.GetVersionsAsync(extension.NuGetId, cancellationToken)
                .ConfigureAwait(false);
            var latest = versions.LastOrDefault();
            var hasUpdate = !string.IsNullOrWhiteSpace(latest)
                && IsNewer(latest!, extension.Version);

            results.Add(new ExtensionUpdateInfo(
                extension.Id,
                extension.NuGetId,
                extension.Version,
                latest,
                hasUpdate));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<Extension?> UpdateAsync(
        Guid extensionId,
        string? version,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        var existing = await extensionRepository.GetByIdAsync(extensionId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        if (!existing.Managed || string.IsNullOrWhiteSpace(existing.NuGetId))
        {
            throw new InvalidOperationException(
                $"Extension '{existing.Name}' is not a NuGet-managed extension and cannot be updated.");
        }

        // Strip the previous staging directory and install record entry so the install hook
        // re-runs when the host loads the new version on restart.
        RemoveExtensionDirectory(existing);
        await RemoveFromInstallRecordAsync(extensionId, cancellationToken).ConfigureAwait(false);

        await extensionRepository.DeleteAsync(extensionId, modifiedByUserId, cancellationToken).ConfigureAwait(false);

        var updated = await InstallFromNuGetAsync(
            existing.NuGetId!,
            version,
            modifiedByUserId,
            cancellationToken).ConfigureAwait(false);
        return updated;
    }

    /// <inheritdoc />
    public async Task<bool> UninstallAsync(
        Guid extensionId,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        var existing = await extensionRepository.GetByIdAsync(extensionId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return false;
        }

        RemoveExtensionDirectory(existing);
        await RemoveFromInstallRecordAsync(extensionId, cancellationToken).ConfigureAwait(false);
        await extensionRepository.DeleteAsync(extensionId, modifiedByUserId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<Extension> StageAndRegisterAsync(
        Stream packageStream,
        string directoryName,
        string? fallbackPackageId,
        bool managed,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.ExtensionsRoot);
        var targetDir = Path.Combine(options.ExtensionsRoot, directoryName);

        if (Directory.Exists(targetDir))
        {
            TryDeleteDirectory(targetDir);
        }

        Directory.CreateDirectory(targetDir);

        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);

        var nuspec = ReadNuspec(archive);
        var libDirectory = ChoosePreferredLibFolder(archive);

        var extractedAssemblyPath = ExtractLibFolder(archive, libDirectory, targetDir);
        ExtractNuspec(archive, targetDir);

        var packageId = nuspec?.Id ?? fallbackPackageId ?? directoryName;
        var packageVersion = nuspec?.Version ?? "0.0.0";
        var assemblyName = extractedAssemblyPath is null
            ? packageId
            : Path.GetFileNameWithoutExtension(extractedAssemblyPath);
        var filePath = extractedAssemblyPath ?? Path.Combine(targetDir, $"{packageId}.dll");

        var entity = new Extension
        {
            Id = Guid.NewGuid(),
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
            LastChecked = DateTime.UtcNow
        };

        var created = await extensionRepository.CreateAsync(entity, modifiedByUserId, cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation(
            "Installed extension '{ExtensionName}' v{ExtensionVersion} into '{TargetDirectory}'. Restart required to load.",
            created.Name,
            created.Version,
            targetDir);
        return created;
    }

    private void RemoveExtensionDirectory(Extension extension)
    {
        if (string.IsNullOrWhiteSpace(extension.FilePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(extension.FilePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        // Only delete directories that live directly under the configured extensions root --
        // never reach outside the staging area, even if a stale row points elsewhere.
        var fullRoot = Path.GetFullPath(options.ExtensionsRoot);
        var fullDir = Path.GetFullPath(directory);
        if (!fullDir.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Refusing to delete extension directory '{Directory}' outside of extensions root '{Root}' ({ExErrorCode}).",
                fullDir,
                fullRoot,
                "extension-uninstall-out-of-root");
            return;
        }

        TryDeleteDirectory(fullDir);
    }

    private void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex)
        {
            // Default ALC keeps assemblies locked on Windows; surface that to the operator.
            logger.LogWarning(
                ex,
                "Failed to delete extension directory '{Directory}'. The extension may be locked by the host process; restart to retry. ({ExErrorCode})",
                directory,
                "extension-directory-delete");
        }
    }

    private async Task RemoveFromInstallRecordAsync(Guid extensionId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(options.ExtensionsRoot, InstalledRecordFileName);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            string[] entries;
            await using (var read = File.OpenRead(path))
            {
                entries = await JsonSerializer
                    .DeserializeAsync<string[]>(read, cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
                    ?? [];
            }

            var key = extensionId.ToString();
            var filtered = entries
                .Where(e => !string.Equals(e, key, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (filtered.Length == entries.Length)
            {
                return;
            }

            await using var write = File.Create(path);
            await JsonSerializer.SerializeAsync(write, filtered, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to update install record while removing extension {ExtensionId} ({ExErrorCode}).",
                extensionId,
                "extension-install-record-update");
        }
    }

    private static NuspecMetadata? ReadNuspec(ZipArchive archive)
    {
        var entry = archive.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)
            && !e.FullName.Contains('/', StringComparison.Ordinal));

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
        catch
        {
            return null;
        }
    }

    private static string? ChoosePreferredLibFolder(ZipArchive archive)
    {
        var libFolders = archive.Entries
            .Where(e => e.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase))
            .Select(e =>
            {
                var parts = e.FullName.Split('/');
                return parts.Length >= 2 ? parts[1] : null;
            })
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

    private static string? ExtractLibFolder(ZipArchive archive, string? libFolder, string targetDir)
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
                var path = ResolveSafeExtractionPath(targetDir, entry.Name, entry);
                ExtractEntry(entry, path);
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
            var path = ResolveSafeExtractionPath(targetDir, relative, entry);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            ExtractEntry(entry, path);

            if (mainDll is null && relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                mainDll = path;
            }
        }

        return mainDll;
    }

    private static void ExtractNuspec(ZipArchive archive, string targetDir)
    {
        var entry = archive.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)
            && !e.FullName.Contains('/', StringComparison.Ordinal));
        if (entry is null)
        {
            return;
        }

        if (IsDirectoryEntry(entry) || string.IsNullOrEmpty(entry.Name))
        {
            return;
        }

        var path = ResolveSafeExtractionPath(targetDir, entry.Name, entry);
        ExtractEntry(entry, path);
    }

    private static void ExtractEntry(ZipArchiveEntry entry, string destinationPath)
    {
        using var input = entry.Open();
        using var output = File.Create(destinationPath);
        input.CopyTo(output);
    }

    /// <summary>
    /// Defends against zip-slip: resolves the candidate extraction path and verifies it is
    /// strictly within <paramref name="targetDir"/>. A crafted package whose entry path contains
    /// <c>..</c> segments or absolute paths will throw rather than escape the staging directory.
    /// </summary>
    private static string ResolveSafeExtractionPath(string targetDir, string relative, ZipArchiveEntry entry)
    {
        var combined = Path.Combine(targetDir, relative);
        var resolvedTarget = Path.GetFullPath(targetDir);
        var resolvedCombined = Path.GetFullPath(combined);
        var sep = Path.DirectorySeparatorChar;
        if (resolvedCombined == resolvedTarget
            || !resolvedCombined.StartsWith(resolvedTarget + sep, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Refusing to extract entry outside target: {entry.FullName}");
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
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray());
        return string.IsNullOrWhiteSpace(sanitized)
            ? $"extension-{Guid.NewGuid():N}"
            : sanitized;
    }

    /// <summary>
    /// Loose ordering check that handles dotted-version strings without requiring a
    /// SemVer parser. Falls back to string comparison if parsing fails.
    /// </summary>
    private static bool IsNewer(string candidate, string current)
    {
        if (Version.TryParse(StripPrerelease(candidate), out var parsedCandidate)
            && Version.TryParse(StripPrerelease(current), out var parsedCurrent))
        {
            return parsedCandidate > parsedCurrent;
        }
        return string.CompareOrdinal(candidate, current) > 0;
    }

    private static string StripPrerelease(string version)
    {
        var dash = version.IndexOf('-');
        var plus = version.IndexOf('+');
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

    private sealed record NuspecMetadata(
        string? Id,
        string? Version,
        string? Title,
        string? TargetAbi,
        string? Authors,
        string? Description);
}
