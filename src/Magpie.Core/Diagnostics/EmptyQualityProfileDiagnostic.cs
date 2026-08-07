using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;

namespace Magpie.Core.Diagnostics;

internal sealed class EmptyQualityProfileDiagnostic(IQualityTierListRepository tierLists) : IDiagnosticCheck
{
    public const string Id = "core.config.empty-quality-profile";

    public string DiagnosticCheckId => Id;

    public string Category => "Quality Tiers";

    public async Task<IReadOnlyList<DiagnosticFinding>> RunAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken = default)
    {
        var lists = await tierLists.ListAllAsync(cancellationToken).ConfigureAwait(false);

        var findings = new List<DiagnosticFinding>();
        foreach (var list in lists)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (list.QualityTierRanks.Count == 0)
            {
                findings.Add(new DiagnosticFinding
                {
                    DiagnosticCheckId = Id,
                    Severity = DiagnosticFindingSeverity.Warning,
                    Title = $"Tier list {list.Name} has no tiers",
                    Detail =
                        $"The tier list {list.Name} has no quality tiers, so no release can match " +
                        "it. Content using this tier list will never satisfy or grab until tiers are added.",
                    AffectedEntity = $"QualityTierList:{list.Id}",
                });
            }
        }

        return findings;
    }
}
