using YamlDotNet.Serialization;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class InfoHashBlock
{
    [YamlMember(Alias = "hash", ApplyNamingConventions = false)]
    public FieldDefinition? Hash { get; set; }

    [YamlMember(Alias = "title", ApplyNamingConventions = false)]
    public FieldDefinition? Title { get; set; }
}
