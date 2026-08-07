using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Magpie.Extensions.Cardigann.Engine;
using Magpie.Models;
using Magpie.Models.Enums;

namespace Magpie.Extensions.Cardigann.Mapping;

internal static class CardigannSettingExtensions
{
    public static IReadOnlyList<SettingSchemaDefinition> ToSettingSchemaDefinitions(
        this IReadOnlyList<CardigannSettingDefinition> settings,
        IReadOnlyList<string> links)
    {
        var declaredKeys = new HashSet<string>();
        var mapped = new List<SettingSchemaDefinition>();

        foreach (var entry in settings)
        {
            if (string.Equals(entry.Type, "info", StringComparison.Ordinal))
            {
                continue;
            }

            mapped.Add(MapOne(entry));
            if (!string.IsNullOrEmpty(entry.Name))
            {
                declaredKeys.Add(entry.Name);
            }
        }

        if (!declaredKeys.Contains(CardigannIndexerIntegration.BaseUrlSettingKey))
        {
            mapped.Add(BuildBaseUrlDefault(links));
        }

        if (!declaredKeys.Contains(CardigannIndexerIntegration.UserAgentSettingKey))
        {
            mapped.Add(BuildUserAgentDefault());
        }

        return mapped;
    }

    private static SettingSchemaDefinition MapOne(CardigannSettingDefinition entry)
    {
        var (dataType, secret) = MapType(entry.Type);
        var options = string.Equals(entry.Type, "select", StringComparison.Ordinal)
            ? CollectOptions(entry.Options)
            : null;

        return new SettingSchemaDefinition
        {
            Key = entry.Name,
            Type = dataType,
            DisplayName = string.IsNullOrWhiteSpace(entry.Label) ? TitleCase(entry.Name) : entry.Label,
            Description = entry.Description,
            Required = entry.Required,
            Secret = secret,
            DefaultValue = StringifyDefault(entry.Default),
            Options = options
        };
    }

    private static (DataType dataType, bool secret) MapType(string yamlType)
    {
        return yamlType switch
        {
            "text" => (DataType.String, false),
            "password" => (DataType.String, true),
            "cookie" => (DataType.String, true),
            "checkbox" => (DataType.Boolean, false),
            "select" => (DataType.String, false),
            "number" => (DataType.Number, false),
            _ => (DataType.String, false)
        };
    }

    private static IReadOnlyList<string> CollectOptions(IReadOnlyDictionary<string, string>? options)
    {
        if (options is null || options.Count == 0)
        {
            return [];
        }

        var list = new List<string>(options.Count);
        foreach (var kvp in options)
        {
            list.Add(kvp.Key);
        }

        return list;
    }

    private static string? StringifyDefault(object? raw)
    {
        if (raw is null)
        {
            return null;
        }

        return raw switch
        {
            string s => s,
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(format: null, CultureInfo.InvariantCulture),
            _ => raw.ToString()
        };
    }

    private static string TitleCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }

    private static SettingSchemaDefinition BuildBaseUrlDefault(IReadOnlyList<string> links)
    {
        return new SettingSchemaDefinition
        {
            Key = CardigannIndexerIntegration.BaseUrlSettingKey,
            Type = DataType.String,
            DisplayName = "Base URL",
            Description = "Override the default tracker URL.",
            Placeholder = links.FirstOrDefault(link => !string.IsNullOrWhiteSpace(link))
        };
    }

    private static SettingSchemaDefinition BuildUserAgentDefault()
    {
        return new SettingSchemaDefinition
        {
            Key = CardigannIndexerIntegration.UserAgentSettingKey,
            Type = DataType.String,
            DisplayName = "User Agent",
            Description = "Override the default User-Agent header sent on requests."
        };
    }
}
