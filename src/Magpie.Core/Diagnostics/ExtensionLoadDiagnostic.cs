using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Extensions;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;

namespace Magpie.Core.Diagnostics;

internal sealed class ExtensionLoadDiagnostic(
    ExtensionLoadContext loadContext,
    IExtensionRepository extensionRepository) : IDiagnosticCheck
{
    public const string Id = "core.extensions.load-failure";

    public string DiagnosticCheckId => Id;

    public string Category => "Extensions";

    public async Task<IReadOnlyList<DiagnosticFinding>> RunAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken = default)
    {
        if (loadContext.Failed.Count == 0)
        {
            return [];
        }

        var enabled = (await extensionRepository.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(extension => extension.Installed && extension.Enabled)
            .Select(extension => extension.Id)
            .ToHashSet();
        return
        [
            .. loadContext.Failed.Where(extension => enabled.Contains(extension.Id)).Select(extension =>
                new DiagnosticFinding
                {
                    DiagnosticCheckId = Id,
                    Severity = DiagnosticFindingSeverity.Error,
                    Title = $"Extension {extension.Name} failed to load",
                    Detail = extension.FilePath,
                    AffectedEntity = $"Extension:{extension.Id}",
                }),
        ];
    }
}
