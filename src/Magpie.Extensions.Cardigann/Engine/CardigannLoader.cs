using System;
using System.Linq;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class CardigannLoader : ICardigannLoader
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(LowerCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static void Validate(CardigannDefinition definition, string origin)
    {
        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            throw new CardigannDefinitionException("'id' is required.", origin);
        }

        if (!definition.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new CardigannDefinitionException(
                $"'id' must be alphanumeric (with optional '-'/'_'); got {definition.Id}.",
                origin);
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new CardigannDefinitionException("'name' is required.", origin);
        }

        if (definition.Links.Count == 0)
        {
            throw new CardigannDefinitionException("'links' must contain at least one URL.", origin);
        }

        if (definition.Search.Paths.Count == 0)
        {
            throw new CardigannDefinitionException("'search.paths' must contain at least one entry.", origin);
        }

        if (string.IsNullOrWhiteSpace(definition.Search.Rows.Selector))
        {
            throw new CardigannDefinitionException("'search.rows.selector' is required.", origin);
        }
    }

    public CardigannDefinition LoadString(string yaml, string origin)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            throw new CardigannDefinitionException("Definition is empty.", origin);
        }

        CardigannDefinition definition;
        try
        {
            definition = _deserializer.Deserialize<CardigannDefinition>(yaml)
                ?? throw new CardigannDefinitionException("Definition deserialised to null.", origin);
        }
        catch (CardigannDefinitionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CardigannDefinitionException("Failed to parse YAML: " + ex.Message, origin, ex);
        }

        Validate(definition, origin);
        return definition;
    }
}
