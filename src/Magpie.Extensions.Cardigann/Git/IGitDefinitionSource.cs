using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.Cardigann.Git;

internal interface IGitDefinitionSource
{
    Task<IReadOnlyDictionary<string, string>> LoadDefinitionsAsync(
        string remoteUrl,
        string branch,
        string definitionsPath,
        string? token,
        CancellationToken cancellationToken = default);
}
