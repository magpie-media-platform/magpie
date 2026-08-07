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

namespace Magpie.Extensions.Tmdb;

internal sealed class AdminUiProvider : IUIExtensionProvider, IAssetProvider, IExtensionSettingsSchemaProvider
{
    private static readonly Guid RouteUIExtensionId = new("9DB88A70-89F5-432F-B8BE-C340D49E2236");
    private static readonly Guid NavUIExtensionId = new("38A1FE64-F1E4-41B5-8750-EB26464EAA7E");
    private static readonly Guid RuntimeAssetId = new("929CFEB2-2FA2-4B52-80C6-9F1AB3F9C97E");
    private static readonly Guid AssetId = new("B7C58F3C-6CE9-4705-8E6D-3A55B04CCB2F");

    private static readonly ExtensionSettingsGroupDefinition SettingsGroupDefinition = new()
    {
        ExtensionId = new Guid("FE120126-EFD7-4DB1-A3A1-8B8A881E013A"),
        Name = Settings.GroupName,
        SettingsSchema =
        [
            new SettingSchemaDefinition
            {
                Key = Settings.ApiKeyKey,
                Type = DataType.String,
                Secret = true
            },
            new SettingSchemaDefinition
            {
                Key = Settings.LanguageKey,
                Type = DataType.String
            }
        ]
    };

    private const string RuntimeAssetPath = "metadata-settings.js";
    private const string RuntimeResourceName = "Magpie.Extensions.Tmdb.Assets.metadata-settings.js";
    private const string TemplateAssetPath = "metadata-settings.html";
    private const string TemplateResourceName = "Magpie.Extensions.Tmdb.Assets.metadata-settings.html";
    private const string AssetPath = "tmdb-settings.js";
    private const string ResourceName = "Magpie.Extensions.Tmdb.Assets.tmdb-settings.js";

    public IEnumerable<UIExtension> GetUIExtensions()
    {
        return
        [
            new UIExtension
            {
                Id = RouteUIExtensionId,
                ExtensionId = new Guid("FE120126-EFD7-4DB1-A3A1-8B8A881E013A"),
                Name = "TMDb Settings",
                Slot = "admin.route.tmdb",
                Modifier = nameof(UISlotModifier.After),
                TagName = "magpie-tmdb-settings",
                Priority = 100
            },
            new UIExtension
            {
                Id = NavUIExtensionId,
                ExtensionId = new Guid("FE120126-EFD7-4DB1-A3A1-8B8A881E013A"),
                Name = "TMDb",
                Slot = "nav.admin",
                Modifier = nameof(UISlotModifier.After),
                TagName = "magpie-nav-item",
                Priority = 100,
                Attributes = new Dictionary<string, string>
                {
                    ["label"] = "TMDb",
                    ["href"] = "/admin/ext/tmdb",
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
                ExtensionId = new Guid("FE120126-EFD7-4DB1-A3A1-8B8A881E013A"),
                Name = "Metadata settings runtime",
                Kind = AssetKind.Script,
                RelativePath = RuntimeAssetPath,
                Priority = 90
            },
            new Asset
            {
                Id = AssetId,
                ExtensionId = new Guid("FE120126-EFD7-4DB1-A3A1-8B8A881E013A"),
                Name = "TMDb settings component",
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

    public Task<Stream?> GetAssetStreamAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        if (string.Equals(relativePath, RuntimeAssetPath, StringComparison.Ordinal))
        {
            var runtimeStream = typeof(AdminUiProvider).Assembly
                .GetManifestResourceStream(RuntimeResourceName);
            return Task.FromResult<Stream?>(runtimeStream);
        }

        if (string.Equals(relativePath, TemplateAssetPath, StringComparison.Ordinal))
        {
            var templateStream = typeof(AdminUiProvider).Assembly
                .GetManifestResourceStream(TemplateResourceName);
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
