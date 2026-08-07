using YamlDotNet.Serialization;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class CategoryMapping
{
    [YamlMember(Alias = "id", ApplyNamingConventions = false)]
    public string Id { get; set; } = string.Empty;

    [YamlMember(Alias = "cat", ApplyNamingConventions = false)]
    public string Cat { get; set; } = string.Empty;
}
