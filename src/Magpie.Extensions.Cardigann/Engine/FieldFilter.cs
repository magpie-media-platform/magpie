using YamlDotNet.Serialization;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class FieldFilter
{
    [YamlMember(Alias = "name", ApplyNamingConventions = false)]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "args", ApplyNamingConventions = false)]
    public object? Args { get; set; }
}
