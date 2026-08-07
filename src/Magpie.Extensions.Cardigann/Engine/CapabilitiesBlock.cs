using System.Collections.Generic;

using YamlDotNet.Serialization;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class CapabilitiesBlock
{
    [YamlMember(Alias = "categorymappings", ApplyNamingConventions = false)]
    public List<CategoryMapping> CategoryMappings { get; set; } = [];

    [YamlMember(Alias = "modes", ApplyNamingConventions = false)]
    public Dictionary<string, List<string>> Modes { get; set; } = [];
}
