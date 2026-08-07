using YamlDotNet.Serialization;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class ResponseBlock
{
    [YamlMember(Alias = "type", ApplyNamingConventions = false)]
    public string? Type { get; set; }
}
