using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;

namespace Magpie.Core.Diagnostics;

internal sealed class DuplicateRootDiagnostic(IContentHierarchyItemRepository items) : IDiagnosticCheck
{
    public const string Id = "core.content.duplicate-root";

    public string DiagnosticCheckId => Id;

    public string Category => "Library";

    public async Task<IReadOnlyList<DiagnosticFinding>> RunAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken = default)
    {
        var duplicates = await items.FindDuplicateRootsAsync(cancellationToken).ConfigureAwait(false);
        return
        [
            .. duplicates
                .SelectMany(duplicate => duplicate.Items)
                .DistinctBy(item => item.Key)
                .Select(item =>
                {
                    var sets = duplicates.Where(duplicate => duplicate.Items.ContainsKey(item.Key)).ToList();
                    var others = sets
                        .SelectMany(duplicate => duplicate.Items)
                        .Where(other => other.Key != item.Key)
                        .DistinctBy(other => other.Key)
                        .Select(other => other.Value)
                        .ToList();
                    var identities = string.Join(", ", sets.Select(set => $"{set.Source}:{set.Value}"));
                    return new DiagnosticFinding
                    {
                        DiagnosticCheckId = Id,
                        Severity = DiagnosticFindingSeverity.Warning,
                        Title = $"{item.Value} is a duplicate",
                        Detail = $"{item.Value} and {string.Join(", ", others)} share {identities}.",
                        AffectedEntity = $"ContentHierarchyItem:{item.Key}",
                    };
                })
        ];
    }
}
