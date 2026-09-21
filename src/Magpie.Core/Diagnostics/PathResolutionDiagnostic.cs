using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Services;

namespace Magpie.Core.Diagnostics;

internal sealed class PathResolutionDiagnostic(
    IRootFolderRepository rootFolders,
    IRemotePathMappingRepository remotePathMappings,
    IRemotePathMappingService remotePathMappingService) : IDiagnosticCheck
{
    public const string Id = "core.config.unresolvable-path";

    public string DiagnosticCheckId => Id;

    public string Category => "Storage";

    public async Task<IReadOnlyList<DiagnosticFinding>> RunAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken = default)
    {
        var findings = new List<DiagnosticFinding>();

        var folders = await rootFolders.ListAllAsync(cancellationToken).ConfigureAwait(false);
        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await remotePathMappingService.TestLocalPathAsync(folder.Path, cancellationToken)
                .ConfigureAwait(false);
            if (!result.Success)
            {
                findings.Add(new DiagnosticFinding
                {
                    DiagnosticCheckId = Id,
                    Severity = DiagnosticFindingSeverity.Warning,
                    Title = $"Root folder {folder.Name} is not reachable",
                    Detail = $"The root folder path {folder.Path} cannot be read: {result.Message}",
                    AffectedEntity = $"{JobDiagnosticAffectedEntityPrefixes.RootFolder}{folder.Id}",
                });
            }
        }

        var mappings = await remotePathMappings.ListAllAsync(cancellationToken).ConfigureAwait(false);
        foreach (var mapping in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await remotePathMappingService.TestLocalPathAsync(mapping.LocalPath, cancellationToken)
                .ConfigureAwait(false);
            if (!result.Success)
            {
                findings.Add(new DiagnosticFinding
                {
                    DiagnosticCheckId = Id,
                    Severity = DiagnosticFindingSeverity.Warning,
                    Title = $"Remote path mapping for {mapping.Host} does not resolve",
                    Detail =
                        $"The local path {mapping.LocalPath} that maps {mapping.RemotePath} cannot be " +
                        $"read: {result.Message}",
                    AffectedEntity = $"RemotePathMapping:{mapping.Id}",
                });
            }
        }

        return findings;
    }
}
