using System.Collections.Generic;

namespace Magpie.Extensions.Tvmaze.Pipeline;

internal sealed record NodeExecutionOutcome
{
    public required int Id { get; init; }

    public required string Kind { get; init; }

    public required string Status { get; init; }

    public string? Reason { get; init; }

    public IReadOnlyList<int> Children { get; init; } = [];

    public IReadOnlyList<string> Queries { get; init; } = [];

    public IReadOnlyList<string> Identities { get; init; } = [];

    public IReadOnlyList<string> Embeds { get; init; } = [];

    public int RowsIn { get; init; }

    public int RowsOut { get; init; }

    public int Rejected { get; init; }

    public int Pages { get; init; }

    public int DetailFanOut { get; init; }
}
