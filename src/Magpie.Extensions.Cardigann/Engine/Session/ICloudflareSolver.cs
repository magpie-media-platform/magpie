using System;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.Cardigann.Engine.Session;

internal interface ICloudflareSolver
{
    Task<CloudflareClearance?> SolveAsync(Uri target, bool reuse, CancellationToken cancellationToken);

    void Forget(Uri target);
}
