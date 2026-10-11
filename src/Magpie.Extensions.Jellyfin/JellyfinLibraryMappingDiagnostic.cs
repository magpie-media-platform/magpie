using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Jellyfin;

internal sealed class JellyfinLibraryMappingDiagnostic(
    ILogger<JellyfinLibraryMappingDiagnostic> logger,
    ISettingsService settingsService) : IDiagnosticCheck
{
    public const string Id = "jellyfin.config.library-path-mismatch";

    public string DiagnosticCheckId => Id;

    public string Category => "Media Servers";

    private static string Normalize(string path) => path.Trim().TrimEnd('/', '\\');

    public async Task<IReadOnlyList<DiagnosticFinding>> RunAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken = default)
    {
        var instanceName = await settingsService.GetInstanceNameAsync(cancellationToken).ConfigureAwait(false);

        var knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in context.RootFolders)
        {
            if (!string.IsNullOrWhiteSpace(root.Path))
            {
                knownPaths.Add(Normalize(root.Path));
            }
        }

        var findings = new List<DiagnosticFinding>();
        foreach (var server in context.MediaServers)
        {
            if (!server.Enabled ||
                !string.Equals(
                    server.ProviderType,
                    Constants.ProviderTypeName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var mappedPath in ReadMappedRootFolderPaths(server))
            {
                if (knownPaths.Contains(Normalize(mappedPath)))
                {
                    continue;
                }

                findings.Add(new DiagnosticFinding
                {
                    DiagnosticCheckId = Id,
                    Severity = DiagnosticFindingSeverity.Warning,
                    Title = $"Jellyfin server {server.Name} maps a library to an unknown root folder",
                    Detail =
                        $"The Jellyfin library mapping points to {mappedPath}, which is not a configured " +
                        $"{instanceName} root folder. Library refreshes and links will apply to the entire server.",
                    AffectedEntity = $"MediaServer:{server.Id}",
                });
            }
        }

        return findings;
    }

    private List<string> ReadMappedRootFolderPaths(MediaServer server)
    {
        var paths = new List<string>();
        foreach (var row in server.ToJellyfinMappingRows(logger))
        {
            var value = row.TryGetValue(Constants.MappingRootFolderPathKey, out var mappedPath)
                ? mappedPath
                : null;
            if (!string.IsNullOrWhiteSpace(value))
            {
                paths.Add(value);
            }
        }

        return paths;
    }
}
