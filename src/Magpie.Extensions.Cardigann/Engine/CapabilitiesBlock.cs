using System.Collections.Generic;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class CapabilitiesBlock
{
    public List<CategoryMapping> CategoryMappings { get; set; } = [];

    public Dictionary<string, List<string>> Modes { get; set; } = [];
}
