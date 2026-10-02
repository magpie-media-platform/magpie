using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.OpenLibrary;

internal sealed class OpenLibraryMisPairedSubjectRepair(
    ISubjectService subjects,
    IMetadataService metadata,
    ILogger<OpenLibraryMisPairedSubjectRepair> logger) : IDiagnosticRepair
{
    public string RepairId => Constants.MisPairedSubjectRepairId;

    public async Task<DiagnosticRepairResult> RunAsync(
        DiagnosticFinding finding,
        CancellationToken cancellationToken = default)
    {
        if (!OpenLibraryMisPairedSubjectDiagnostic.TryParseAffectedEntity(finding.AffectedEntity, out var subjectId))
        {
            return DiagnosticRepairResult.Failed("The finding names no subject.");
        }

        var contributions = await metadata.GetSubjectContributionsAsync(subjectId, cancellationToken)
            .ConfigureAwait(false);
        if (contributions is null)
        {
            return DiagnosticRepairResult.Failed("The subject no longer exists.");
        }

        if (OpenLibraryMisPairedSubjectDiagnostic.ReadOpenLibrarySubject(contributions) is not { } pair)
        {
            return DiagnosticRepairResult.Succeeded("The subject no longer holds an Open Library key.");
        }

        if (!OpenLibraryMisPairedSubjectDiagnostic.IsMisPaired(pair.Name, pair.Key))
        {
            return DiagnosticRepairResult.Succeeded($"The Open Library key {pair.Key} matches the name {pair.Name}.");
        }

        var edited = await OpenLibraryMisPairedSubjectDiagnostic
            .ListUserEditedIdsAsync(subjects, [subjectId], cancellationToken)
            .ConfigureAwait(false);
        if (pair.HasPrimaryImage || edited.Count > 0)
        {
            return DiagnosticRepairResult.Failed($"The subject {pair.Name} has user edits.");
        }

        if (!await subjects.DeleteAsync(subjectId, null, cancellationToken).ConfigureAwait(false))
        {
            return DiagnosticRepairResult.Failed("The subject no longer exists.");
        }

        logger.LogInformation(
            "The Open Library repair deleted subject {SubjectId} {SubjectName} with the mis-paired key {Key}",
            subjectId,
            pair.Name,
            pair.Key);
        return DiagnosticRepairResult.Succeeded(
            $"Deleted the subject {pair.Name} with the mis-paired Open Library key {pair.Key}.");
    }
}
