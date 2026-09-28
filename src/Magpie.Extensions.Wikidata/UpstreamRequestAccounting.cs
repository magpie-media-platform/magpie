using System;
using System.Threading;

namespace Magpie.Extensions.Wikidata;

internal sealed class UpstreamRequestAccounting : IDisposable
{
    private static readonly AsyncLocal<UpstreamRequestAccounting?> Current = new();

    private readonly UpstreamRequestAccounting? _previous;
    private readonly Action _observer;

    internal UpstreamRequestAccounting(Action observer)
    {
        _previous = Current.Value;
        _observer = observer;
        Current.Value = this;
    }

    internal static Action? Observer => Current.Value?._observer;

    internal static void Record(Action? fallback) => (Observer ?? fallback)?.Invoke();

    /// <inheritdoc />
    public void Dispose() => Current.Value = _previous;
}
