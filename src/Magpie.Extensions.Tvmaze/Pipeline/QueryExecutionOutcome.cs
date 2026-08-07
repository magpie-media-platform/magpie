using System.Collections.Generic;

namespace Magpie.Extensions.Tvmaze.Pipeline;

internal sealed record QueryExecutionOutcome
{
    public string Status { get; set; } = "not-started";

    public int MaximumResults { get; init; }

    public int RowsIn { get; set; }

    public int RowsOut { get; set; }

    public int Pages { get; set; }

    public int DetailFanOut { get; set; }

    public double ElapsedMilliseconds { get; set; }

    public bool HasMore { get; set; }

    public IReadOnlyList<string> Warnings { get; set; } = [];

    public List<NodeExecutionOutcome> Branches { get; } = [];

    public Dictionary<string, int> Rejections { get; } = [];

    public List<WireAttemptOutcome> WireAttempts { get; } = [];
}
