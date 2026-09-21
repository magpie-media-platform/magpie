using System;
using System.Collections.Generic;

using Magpie.Models;

namespace Magpie.Api;

internal static class SettingsExtensions
{
    public static Dictionary<string, string> RedactSecrets(
        this Dictionary<string, string> settings,
        IReadOnlyList<SettingSchemaDefinition> schema)
    {
        if (schema.Count == 0)
        {
            return [];
        }

        var secretKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in schema)
        {
            if (definition.Secret)
            {
                secretKeys.Add(definition.Key);
            }
        }

        var redacted = new Dictionary<string, string>();
        foreach (var entry in settings)
        {
            if (!secretKeys.Contains(entry.Key))
            {
                redacted[entry.Key] = entry.Value;
            }
        }

        return redacted;
    }
}
