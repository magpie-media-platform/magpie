using System.Text.Json;

namespace Magpie.Extensions.Wikidata.Mapping;

internal sealed record DataValueDto
{
    public JsonElement Value { get; init; }
}
