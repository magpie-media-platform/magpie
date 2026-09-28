using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;

namespace Magpie.Extensions.Tmdb;

internal sealed class ApiKeyMissingDiagnostic(ISettingsResolver resolver) : IDiagnosticCheck
{
    public const string Id = "tmdb.config.api-key-missing";

    public string DiagnosticCheckId => Id;

    public string Category => "Metadata";

    public async Task<IReadOnlyList<DiagnosticFinding>> RunAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await resolver.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.IsConfigured)
        {
            return [];
        }

        var extensionId = Constants.ExtensionId;
        return
        [
            new DiagnosticFinding
            {
                DiagnosticCheckId = Id,
                Severity = DiagnosticFindingSeverity.Warning,
                Title = "TMDb API key is not configured",
                Detail =
                    $"The TMDb metadata provider has no API key in settings group {Settings.GroupName}, key " +
                    $"{Settings.ApiKeyKey}, or in the {SettingsResolver.EnvApiKeyVariable} environment variable, " +
                    "so movie and TV metadata lookups return nothing.",
                AffectedEntity = $"Extension:{extensionId}",
            },
        ];
    }
}
