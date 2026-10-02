using System.Collections.Generic;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class SearchSection
{
    public List<SearchPath> Paths { get; set; } = [];

    public Dictionary<string, string> Inputs { get; set; } = [];

    public RowsSelector Rows { get; set; } = new();

    public Dictionary<string, FieldDefinition> Fields { get; set; } = [];
}
