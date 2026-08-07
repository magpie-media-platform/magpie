using System.Collections.Generic;

using YamlDotNet.Serialization;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class DownloadBlock
{
    [YamlMember(Alias = "selectors", ApplyNamingConventions = false)]
    public List<FieldDefinition> Selectors { get; set; } = [];

    [YamlMember(Alias = "selector", ApplyNamingConventions = false)]
    public string? Selector { get; set; }

    [YamlMember(Alias = "attribute", ApplyNamingConventions = false)]
    public string? Attribute { get; set; }

    [YamlMember(Alias = "infohash", ApplyNamingConventions = false)]
    public InfoHashBlock? InfoHash { get; set; }

    [YamlMember(Alias = "filters", ApplyNamingConventions = false)]
    public List<FieldFilter>? Filters { get; set; }

    [YamlMember(Alias = "method", ApplyNamingConventions = false)]
    public string? Method { get; set; }
}
