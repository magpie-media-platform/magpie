using System.Collections.Generic;

using YamlDotNet.Serialization;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class FieldDefinition
{
    [YamlMember(Alias = "selector", ApplyNamingConventions = false)]
    public string? Selector { get; set; }

    [YamlMember(Alias = "attribute", ApplyNamingConventions = false)]
    public string? Attribute { get; set; }

    [YamlMember(Alias = "text", ApplyNamingConventions = false)]
    public string? Text { get; set; }

    [YamlMember(Alias = "case", ApplyNamingConventions = false)]
    public Dictionary<string, string>? Case { get; set; }

    [YamlMember(Alias = "optional", ApplyNamingConventions = false)]
    public bool Optional { get; set; }

    [YamlMember(Alias = "default", ApplyNamingConventions = false)]
    public string? Default { get; set; }

    [YamlMember(Alias = "filters", ApplyNamingConventions = false)]
    public List<FieldFilter>? Filters { get; set; }
}
