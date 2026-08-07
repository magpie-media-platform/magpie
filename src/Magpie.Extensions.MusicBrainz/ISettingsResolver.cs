using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.MusicBrainz;

internal interface ISettingsResolver
{
    Task<Settings> ResolveAsync(CancellationToken cancellationToken);
}
