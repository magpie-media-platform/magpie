namespace Magpie.Extensions.Tvmaze.Pipeline;

internal sealed record WireAttemptOutcome
{
    public int Sequence { get; init; }

    public required string Method { get; init; }

    public required string Url { get; init; }

    public int Attempt { get; init; }

    public int? StatusCode { get; init; }

    public bool Redirect { get; init; }

    public bool Retry { get; init; }

    public string? Failure { get; init; }
}
