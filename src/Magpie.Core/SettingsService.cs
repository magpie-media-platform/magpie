using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Patch;
using Magpie.Sdk.Configuration;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core;

internal sealed class SettingsService(
    ISettingsRepository settingsRepository,
    IEnumerable<IExtensionSettingsSchemaProvider> schemaProviders,
    IEnumerable<IExtensionConfigurationTester> configurationTesters,
    ILogger<SettingsService> logger,
    IExtensionRepository? extensionRepository = null) : ISettingsService
{
    private static readonly IReadOnlyList<string> SystemSettingsGroupNames = [MonitoringGroupName, SetupGroupName];
    private const string MonitoringGroupName = "monitoring";
    private const string SetupGroupName = "setup";

    private static IntegrationSettingsValidationResult Validate(
        IReadOnlyList<SettingSchemaDefinition> schema,
        IReadOnlyDictionary<string, string> settings)
    {
        var issues = new List<IntegrationSettingsValidationIssue>();

        var schemaByKey = schema.ToDictionary(definition => definition.Key, StringComparer.OrdinalIgnoreCase);

        var settingsByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in settings)
        {
            settingsByKey[entry.Key] = entry.Value;
        }

        foreach (var definition in schema)
        {
            if (!definition.Required)
            {
                continue;
            }

            if (!settingsByKey.TryGetValue(definition.Key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                issues.Add(new(definition.Key, $"{Label(definition)} is required."));
            }
        }

        foreach (var entry in settingsByKey)
        {
            if (!schemaByKey.TryGetValue(entry.Key, out var definition) || string.IsNullOrWhiteSpace(entry.Value))
            {
                continue;
            }

            if (!IsValueOfType(entry.Value, definition.Type))
            {
                issues.Add(new IntegrationSettingsValidationIssue(definition.Key, TypeMismatchMessage(definition)));
                continue;
            }

            if (definition.Options is { Count: > 0 } options &&
                !options.Contains(entry.Value, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new IntegrationSettingsValidationIssue(
                    definition.Key,
                    $"{Label(definition)} must be one of: {string.Join(", ", options)}."));
            }

            if (definition.Type == DataType.MappingsList &&
                definition.RowSchema is { Count: > 0 } rowSchema &&
                TryParseMappingRows(entry.Value, out var rows))
            {
                for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    foreach (var field in rowSchema)
                    {
                        if (!field.Required)
                        {
                            continue;
                        }

                        if (!rows[rowIndex].TryGetValue(field.Key, out var cell) || string.IsNullOrWhiteSpace(cell))
                        {
                            issues.Add(new IntegrationSettingsValidationIssue(
                                $"{definition.Key}[{rowIndex}].{field.Key}",
                                $"{Label(definition)} row {rowIndex + 1}: {Label(field)} is required."));
                        }
                    }
                }
            }
        }

        return new IntegrationSettingsValidationResult
        {
            IsValid = issues.Count == 0,
            Issues = issues
        };
    }

    private static string Label(SettingSchemaDefinition definition)
        => string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.Key : definition.DisplayName!;

    private static string Label(SettingSchemaField field)
        => string.IsNullOrWhiteSpace(field.DisplayName) ? field.Key : field.DisplayName!;

    private static string TypeMismatchMessage(SettingSchemaDefinition definition)
    {
        var label = Label(definition);
        return definition.Type switch
        {
            DataType.Number => $"{label} must be a number.",
            DataType.Boolean => $"{label} must be true or false.",
            DataType.Date => $"{label} must be a valid date.",
            DataType.MappingsList => $"{label} must be a JSON array of mapping rows.",
            _ => $"{label} is invalid."
        };
    }

    private static bool IsValueOfType(string value, DataType type)
    {
        return type switch
        {
            DataType.String => true,
            DataType.Number => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
            DataType.Boolean => value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                value == "1" ||
                value == "0",
            DataType.Date => DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out _),
            DataType.MappingsList => TryParseMappingRows(value, out _),
            _ => false
        };
    }

    private static bool TryParseMappingRows(string value, out List<Dictionary<string, string>> rows)
    {
        rows = [];
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in element.EnumerateObject())
                {
                    row[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                        JsonValueKind.Null => string.Empty,
                        _ => property.Value.GetRawText()
                    };
                }

                rows.Add(row);
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Dictionary<string, string> ToSettings(SettingsGroup? group)
    {
        return group?.Settings.ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal)
            ?? new Dictionary<string, string>();
    }

    private static SettingsGroupDetail BuildDetail(
        string name,
        IReadOnlyDictionary<string, string> settings,
        Guid? extensionId,
        IReadOnlyList<SettingSchemaDefinition>? schema,
        bool schemaResolved,
        bool canTest)
    {
        var entity = new SettingsGroup
        {
            Name = name,
            ExtensionId = extensionId
        };
        foreach (var entry in settings)
        {
            entity.Settings.Add(new Setting { Key = entry.Key, Value = entry.Value });
        }

        return new SettingsGroupDetail
        {
            Group = entity,
            Schema = schema ?? [],
            SchemaResolved = schemaResolved,
            CanTest = canTest
        };
    }

    private static void ApplyPatch(IDictionary<string, string> settings, DictionaryPatch<string, string, string> patch)
    {
        if (patch.Remove is not null)
        {
            foreach (var key in patch.Remove)
            {
                settings.Remove(key);
            }
        }

        if (patch.Add is not null)
        {
            foreach (var entry in patch.Add)
            {
                settings[entry.Key] = entry.Value;
            }
        }

        if (patch.Update is not null)
        {
            foreach (var entry in patch.Update)
            {
                settings[entry.Key] = entry.Value;
            }
        }
    }

    public async Task<IReadOnlyList<SettingsGroupDetail>> ListGroupsAsync(CancellationToken cancellationToken = default)
    {
        var details = new List<SettingsGroupDetail>();

        var systemGroupNames = new HashSet<string>();
        var allGroups = await settingsRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        foreach (var group in allGroups.Where(g => g.ExtensionId is null))
        {
            systemGroupNames.Add(group.Name);
            details.Add(BuildDetail(
                group.Name,
                ToSettings(group),
                extensionId: null,
                schema: [],
                schemaResolved: true,
                canTest: false));
        }

        foreach (var name in SystemSettingsGroupNames)
        {
            if (systemGroupNames.Add(name))
            {
                details.Add(BuildDetail(
                    name,
                    new Dictionary<string, string>(),
                    extensionId: null,
                    schema: [],
                    schemaResolved: true,
                    canTest: false));
            }
        }

        foreach (var binding in await GetExtensionSettingsGroupBindingsAsync(cancellationToken).ConfigureAwait(false))
        {
            var definition = binding.Definition;
            var group = await settingsRepository
                .GetByIdAsync(new(binding.LoadedExtensionId, definition.Name), cancellationToken)
                .ConfigureAwait(false);
            details.Add(BuildDetail(
                definition.Name,
                ToSettings(group),
                binding.LoadedExtensionId,
                definition.SettingsSchema,
                schemaResolved: true,
                canTest: configurationTesters.Any(tester =>
                    tester.ExtensionId == definition.ExtensionId &&
                    string.Equals(tester.SettingsGroupName, definition.Name, StringComparison.Ordinal))));
        }

        return details;
    }

    public async Task<SettingsGroupDetail?> GetSystemGroupAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var group = await settingsRepository
            .GetByIdAsync(new(null, name), cancellationToken)
            .ConfigureAwait(false);
        if (group is not null)
        {
            return BuildDetail(
                group.Name,
                ToSettings(group),
                extensionId: null,
                schema: [],
                schemaResolved: true,
                canTest: false);
        }

        return SystemSettingsGroupNames.Contains(name, StringComparer.Ordinal)
            ? BuildDetail(
                name,
                new Dictionary<string, string>(),
                extensionId: null,
                schema: [],
                schemaResolved: true,
                canTest: false)
            : null;
    }

    public async Task<SettingsGroupDetail?> GetExtensionGroupAsync(
        Guid extensionId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var binding = await FindExtensionSettingsGroupBindingAsync(extensionId, name, cancellationToken)
            .ConfigureAwait(false);
        var settingsExtensionId = binding?.LoadedExtensionId ?? extensionId;
        var responseExtensionId = binding?.LoadedExtensionId ?? extensionId;
        var schema = binding?.Definition.SettingsSchema;

        var group = await settingsRepository
            .GetByIdAsync(new SettingsGroupLookupKey(settingsExtensionId, name), cancellationToken)
            .ConfigureAwait(false);
        var settings = ToSettings(group);

        if (schema is null && settings.Count == 0)
        {
            return null;
        }

        var testerExtensionId = binding?.Definition.ExtensionId ?? extensionId;
        return BuildDetail(
            name,
            settings,
            responseExtensionId,
            schema,
            schemaResolved: schema is not null,
            canTest: configurationTesters.Any(tester =>
                tester.ExtensionId == testerExtensionId &&
                string.Equals(tester.SettingsGroupName, name, StringComparison.Ordinal)));
    }

    public async Task<bool> PatchSystemGroupAsync(
        string name,
        SettingsGroupPatch patch,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);

        return await WriteGroupAsync(extensionId: null, name, patch.Settings, callerUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> PatchExtensionGroupAsync(
        Guid extensionId,
        string name,
        SettingsGroupPatch patch,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var binding = await FindExtensionSettingsGroupBindingAsync(extensionId, name, cancellationToken)
            .ConfigureAwait(false);

        return await WriteGroupAsync(
            binding?.LoadedExtensionId ?? extensionId,
            name,
            patch.Settings,
            callerUserId,
            cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ConfigurationTestResult?> TestGroupAsync(
        Guid extensionId,
        string name,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        var binding = await FindExtensionSettingsGroupBindingAsync(extensionId, name, cancellationToken)
            .ConfigureAwait(false);
        var settingsExtensionId = binding?.Definition.ExtensionId ?? extensionId;
        var tester = configurationTesters.FirstOrDefault(t =>
            t.ExtensionId == settingsExtensionId &&
            string.Equals(t.SettingsGroupName, name, StringComparison.Ordinal));

        if (tester is null)
        {
            logger.LogDebug(
                "The settings group {SettingsGroupName} of extension {ExtensionId} has no configuration tester",
                name,
                settingsExtensionId);
            return null;
        }

        return await tester.TestConfigurationAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IntegrationSettingsValidationResult> ValidateAsync(
        IReadOnlyList<SettingSchemaDefinition> schema,
        IReadOnlyDictionary<string, string> settings,
        object integration,
        CancellationToken cancellationToken = default)
    {
        var baseline = Validate(schema, settings);

        if (integration is not IValidatesIntegrationSettings extensionValidator)
        {
            return baseline;
        }

        var extensionResult = await extensionValidator
            .ValidateSettingsAsync(settings, cancellationToken)
            .ConfigureAwait(false);

        return new IntegrationSettingsValidationResult
        {
            IsValid = baseline.IsValid && extensionResult.IsValid,
            Issues = [.. baseline.Issues, .. extensionResult.Issues]
        };
    }

    private async Task<bool> WriteGroupAsync(
        Guid? extensionId,
        string name,
        DictionaryPatch<string, string, string> patch,
        Guid callerUserId,
        CancellationToken cancellationToken)
    {
        var group = await settingsRepository
            .GetByIdAsync(new SettingsGroupLookupKey(extensionId, name), cancellationToken)
            .ConfigureAwait(false);
        var current = ToSettings(group);

        var desired = new Dictionary<string, string>(current);
        ApplyPatch(desired, patch);

        var add = desired
            .Where(e => !current.ContainsKey(e.Key))
            .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
        var update = desired
            .Where(e => current.TryGetValue(e.Key, out var original) && original != e.Value)
            .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
        var remove = current.Keys.Except(desired.Keys, StringComparer.Ordinal).ToArray();

        if (add.Count == 0 && update.Count == 0 && remove.Length == 0)
        {
            return false;
        }

        return await settingsRepository.PatchAsync(
            new SettingsGroupPatch
            {
                ExtensionId = extensionId,
                Name = name,
                Settings = new DictionaryPatch<string, string, string>
                {
                    Add = add.Count > 0 ? add : null,
                    Remove = remove.Length > 0 ? remove : null,
                    Update = update.Count > 0 ? update : null
                }
            },
            callerUserId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ExtensionSettingsGroupBinding?> FindExtensionSettingsGroupBindingAsync(
        Guid extensionId,
        string name,
        CancellationToken cancellationToken)
    {
        var bindings = await GetExtensionSettingsGroupBindingsAsync(cancellationToken).ConfigureAwait(false);
        return bindings.FirstOrDefault(binding =>
            (binding.LoadedExtensionId == extensionId || binding.Definition.ExtensionId == extensionId) &&
            string.Equals(binding.Definition.Name, name, StringComparison.Ordinal));
    }

    private async Task<IReadOnlyList<ExtensionSettingsGroupBinding>> GetExtensionSettingsGroupBindingsAsync(
        CancellationToken cancellationToken)
    {
        if (extensionRepository is null)
        {
            return
            [
                .. schemaProviders.SelectMany(provider => provider.GetSettingsGroups())
                    .Select(definition => new ExtensionSettingsGroupBinding(definition, definition.ExtensionId))
            ];
        }

        var extensions = await extensionRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        var extensionsByAssemblyName = extensions
            .ToLookup(extension => extension.AssemblyName, StringComparer.OrdinalIgnoreCase);
        var bindings = new List<ExtensionSettingsGroupBinding>();

        foreach (var provider in schemaProviders)
        {
            var assemblyName = provider.GetType().Assembly.GetName().Name;
            var loadedExtension = string.IsNullOrWhiteSpace(assemblyName)
                ? null
                : extensionsByAssemblyName[assemblyName].FirstOrDefault();

            foreach (var definition in provider.GetSettingsGroups())
            {
                bindings.Add(new ExtensionSettingsGroupBinding(
                    definition,
                    loadedExtension?.Id ?? definition.ExtensionId));
            }
        }

        return bindings;
    }

    private sealed record ExtensionSettingsGroupBinding(
        ExtensionSettingsGroupDefinition Definition,
        Guid LoadedExtensionId);
}
