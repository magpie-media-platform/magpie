using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.Wikidata;

internal interface ISettingsResolver
{
    Task<RequestContext> ResolveAsync(string? defaultLanguagePreference, CancellationToken cancellationToken);
}
