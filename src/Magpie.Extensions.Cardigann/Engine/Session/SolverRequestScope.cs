using System;
using System.Threading;

namespace Magpie.Extensions.Cardigann.Engine.Session;

internal sealed class SolverRequestScope : IDisposable
{
    private static readonly AsyncLocal<SolverRequestKind> Kind = new();
    private readonly SolverRequestKind _previous;

    public SolverRequestScope(SolverRequestKind kind)
    {
        _previous = Kind.Value;
        Kind.Value = kind;
    }

    public static SolverRequestKind Current => Kind.Value;

    public void Dispose() => Kind.Value = _previous;
}
