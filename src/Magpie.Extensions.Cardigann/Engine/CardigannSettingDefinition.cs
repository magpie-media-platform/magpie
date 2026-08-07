using System.Collections.Generic;

using YamlDotNet.Serialization;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class CardigannSettingDefinition
{
    [YamlMember(Alias = "name", ApplyNamingConventions = false)]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "type", ApplyNamingConventions = false)]
    public string Type { get; set; } = string.Empty;

    [YamlMember(Alias = "label", ApplyNamingConventions = false)]
    public string? Label { get; set; }

    [YamlMember(Alias = "description", ApplyNamingConventions = false)]
    public string? Description { get; set; }

    [YamlMember(Alias = "default", ApplyNamingConventions = false)]
    public object? Default { get; set; }

    [YamlMember(Alias = "required", ApplyNamingConventions = false)]
    public bool Required { get; set; }

    [YamlMember(Alias = "options", ApplyNamingConventions = false)]
    public Dictionary<string, string>? Options { get; set; }
}
