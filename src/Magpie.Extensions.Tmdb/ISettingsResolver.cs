using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.Tmdb;

internal interface ISettingsResolver
{
    Task<Settings> ReadSnapshotAsync(CancellationToken cancellationToken);

    Task<RequestContext?> AcquireContextAsync(string? defaultLanguagePreference, CancellationToken cancellationToken);

    Task<RequestContext?> AcquireContextAsync(
        IEnumerable<string>? ancestorLanguagePreferences,
        string? defaultLanguagePreference,
        CancellationToken cancellationToken);
}
