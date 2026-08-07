using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary.QueryEngine;

namespace Magpie.Extensions.OpenLibrary;

internal interface ISettingsResolver
{
    Task<RequestContext> GetRequestContextAsync(CancellationToken cancellationToken);
}
