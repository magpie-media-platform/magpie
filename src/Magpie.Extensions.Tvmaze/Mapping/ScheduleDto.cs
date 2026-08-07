using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal sealed record ScheduleDto
{
    [JsonPropertyName("time")]
    public string? Time { get; init; }

    [JsonPropertyName("days")]
    public IReadOnlyList<string>? Days { get; init; }
}
