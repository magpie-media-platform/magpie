using System;
using System.Collections.Generic;
using System.Linq;

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

        var secretKeys = schema.Where(d => d.Secret).Select(d => d.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return settings.Where(entry => !secretKeys.Contains(entry.Key)).ToDictionary();
    }
}
