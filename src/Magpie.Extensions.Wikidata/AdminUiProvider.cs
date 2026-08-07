using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Models.UI;
using Magpie.Sdk.Configuration;
using Magpie.Sdk.UI;

namespace Magpie.Extensions.Wikidata;

internal sealed class AdminUiProvider : IUIExtensionProvider, IAssetProvider, IExtensionSettingsSchemaProvider
{
    private static readonly Guid ExtensionId = new("A70A2CC8-9BC7-4E24-9584-A4A458E4FCFD");
    private static readonly Guid RouteUIExtensionId = new("132321CA-BEDF-47DB-BC25-C29CAAE0F6C0");
    private static readonly Guid NavUIExtensionId = new("C1CABFCB-AF7C-4E55-815A-24F31928291E");
    private static readonly Guid RuntimeAssetId = new("A1A64825-DDE7-4579-B220-D0932E848500");
    private static readonly Guid AssetId = new("DF4F8760-B134-42F0-AE9E-A84BAD782077");

    private static readonly ExtensionSettingsGroupDefinition SettingsGroupDefinition = new()
    {
        ExtensionId = ExtensionId,
        Name = Settings.GroupName,
        SettingsSchema =
        [
            new SettingSchemaDefinition
            {
                Key = Settings.ContactKey,
                Type = DataType.String
            },
            new SettingSchemaDefinition
            {
                Key = Settings.LanguageKey,
                Type = DataType.String
            }
        ]
    };

    private const string RuntimeAssetPath = "metadata-settings.js";
    private const string RuntimeResourceName = "Magpie.Extensions.Wikidata.Assets.metadata-settings.js";
    private const string TemplateAssetPath = "metadata-settings.html";
    private const string TemplateResourceName = "Magpie.Extensions.Wikidata.Assets.metadata-settings.html";
    private const string AssetPath = "wikidata-settings.js";
    private const string ResourceName = "Magpie.Extensions.Wikidata.Assets.wikidata-settings.js";

    public IEnumerable<UIExtension> GetUIExtensions()
    {
        return
        [
            new UIExtension
            {
                Id = RouteUIExtensionId,
                ExtensionId = ExtensionId,
                Name = "Wikidata Settings",
                Slot = "admin.route.wikidata",
                Modifier = nameof(UISlotModifier.After),
                TagName = "magpie-wikidata-settings",
                Priority = 100
            },
            new UIExtension
            {
                Id = NavUIExtensionId,
                ExtensionId = ExtensionId,
                Name = "Wikidata",
                Slot = "nav.admin",
                Modifier = nameof(UISlotModifier.After),
                TagName = "magpie-nav-item",
                Priority = 100,
                Attributes = new Dictionary<string, string>
                {
                    ["label"] = "Wikidata",
                    ["href"] = "/admin/ext/wikidata",
                    ["icon"] = "database"
                }
            }
        ];
    }

    public IEnumerable<Asset> ListAssets()
    {
        return
        [
            new Asset
            {
                Id = RuntimeAssetId,
                ExtensionId = ExtensionId,
                Name = "Metadata settings runtime",
                Kind = AssetKind.Script,
                RelativePath = RuntimeAssetPath,
                Priority = 90
            },
            new Asset
            {
                Id = AssetId,
                ExtensionId = ExtensionId,
                Name = "Wikidata settings component",
                Kind = AssetKind.Script,
                RelativePath = AssetPath,
                Priority = 100
            }
        ];
    }

    public IEnumerable<ExtensionSettingsGroupDefinition> GetSettingsGroups()
    {
        return [SettingsGroupDefinition];
    }

    public Task<Stream?> GetAssetStreamAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(relativePath, RuntimeAssetPath, StringComparison.Ordinal))
        {
            var runtimeStream = typeof(AdminUiProvider).Assembly.GetManifestResourceStream(RuntimeResourceName);
            return Task.FromResult<Stream?>(runtimeStream);
        }

        if (string.Equals(relativePath, TemplateAssetPath, StringComparison.Ordinal))
        {
            var templateStream = typeof(AdminUiProvider).Assembly.GetManifestResourceStream(TemplateResourceName);
            return Task.FromResult<Stream?>(templateStream);
        }

        if (!string.Equals(relativePath, AssetPath, StringComparison.Ordinal))
        {
            return Task.FromResult<Stream?>(null);
        }

        var stream = typeof(AdminUiProvider).Assembly.GetManifestResourceStream(ResourceName);
        return Task.FromResult<Stream?>(stream);
    }
}
