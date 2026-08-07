using System;
using System.Globalization;
using System.Threading;

namespace Magpie.Core;

internal sealed class StateManager<TState>(TState initial) where TState : struct, Enum
{
    private volatile int _state = Convert.ToInt32(initial, CultureInfo.InvariantCulture);

    public bool TryTransition(TState to, params TState[] from)
    {
        var toInt = Convert.ToInt32(to, CultureInfo.InvariantCulture);

        foreach (var fromState in from)
        {
            var fromInt = Convert.ToInt32(fromState, CultureInfo.InvariantCulture);
            if (Interlocked.CompareExchange(ref _state, toInt, fromInt) == fromInt)
            {
                return true;
            }
        }

        return false;
    }
}
