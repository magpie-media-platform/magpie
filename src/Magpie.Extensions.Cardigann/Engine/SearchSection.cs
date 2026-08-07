using System.Collections.Generic;

using YamlDotNet.Serialization;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class SearchSection
{
    [YamlMember(Alias = "paths", ApplyNamingConventions = false)]
    public List<SearchPath> Paths { get; set; } = [];

    [YamlMember(Alias = "inputs", ApplyNamingConventions = false)]
    public Dictionary<string, string> Inputs { get; set; } = [];

    [YamlMember(Alias = "rows", ApplyNamingConventions = false)]
    public RowsSelector Rows { get; set; } = new();

    [YamlMember(Alias = "fields", ApplyNamingConventions = false)]
    public Dictionary<string, FieldDefinition> Fields { get; set; } = [];
}
