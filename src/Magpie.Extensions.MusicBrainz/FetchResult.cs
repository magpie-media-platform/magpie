namespace Magpie.Extensions.MusicBrainz;

internal sealed record FetchResult<T>
    where T : class
{
    public required QueryEngine.FetchOutcome Outcome { get; init; }

    public T? Value { get; init; }

    public int? StatusCode { get; init; }
}
