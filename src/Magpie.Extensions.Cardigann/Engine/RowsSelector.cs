using YamlDotNet.Serialization;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class RowsSelector
{
    [YamlMember(Alias = "selector", ApplyNamingConventions = false)]
    public string Selector { get; set; } = string.Empty;
}
