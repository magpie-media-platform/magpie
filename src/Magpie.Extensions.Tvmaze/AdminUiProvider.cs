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

namespace Magpie.Extensions.Tvmaze;

internal sealed class AdminUiProvider : IUIExtensionProvider, IAssetProvider, IExtensionSettingsSchemaProvider
{
    private static readonly Guid RouteUIExtensionId = new("65034AEE-1D0F-4F31-B7CD-504A1E131E04");
    private static readonly Guid NavUIExtensionId = new("C20D390F-C7B2-409B-9591-85A671D7F52B");
    private static readonly Guid RuntimeAssetId = new("90199AA9-E9E7-4528-BD1B-A4FDD14A8C4A");
    private static readonly Guid AssetId = new("94B5DE2C-F311-4394-ADAF-195E029DBAE6");

    private static readonly ExtensionSettingsGroupDefinition SettingsGroupDefinition = new()
    {
        ExtensionId = Constants.ExtensionId,
        Name = ProviderSettings.GroupName,
        SettingsSchema =
        [new SettingSchemaDefinition { Key = ProviderSettings.ContactKey, Type = DataType.String }]
    };

    private const string RuntimeAssetPath = "metadata-settings.js";
    private const string RuntimeResourceName = "Magpie.Extensions.Tvmaze.Assets.metadata-settings.js";
    private const string TemplateAssetPath = "metadata-settings.html";
    private const string TemplateResourceName = "Magpie.Extensions.Tvmaze.Assets.metadata-settings.html";
    private const string AssetPath = "tvmaze-settings.js";
    private const string ResourceName = "Magpie.Extensions.Tvmaze.Assets.tvmaze-settings.js";

    public IEnumerable<UIExtension> GetUIExtensions()
    {
        return
        [
            new UIExtension
            {
                Id = RouteUIExtensionId,
                ExtensionId = Constants.ExtensionId,
                Name = "TVmaze Settings",
                Slot = "admin.route.tvmaze",
                Modifier = nameof(UISlotModifier.After),
                TagName = "magpie-tvmaze-settings",
                Priority = 100
            },
            new UIExtension
            {
                Id = NavUIExtensionId,
                ExtensionId = Constants.ExtensionId,
                Name = "TVmaze",
                Slot = "nav.admin",
                Modifier = nameof(UISlotModifier.After),
                TagName = "magpie-nav-item",
                Priority = 100,
                Attributes = new Dictionary<string, string>
                {
                    ["label"] = "TVmaze",
                    ["href"] = "/admin/ext/tvmaze",
                    ["icon"] = "television"
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
                ExtensionId = Constants.ExtensionId,
                Name = "Metadata settings runtime",
                Kind = AssetKind.Script,
                RelativePath = RuntimeAssetPath,
                Priority = 90
            },
            new Asset
            {
                Id = AssetId,
                ExtensionId = Constants.ExtensionId,
                Name = "TVmaze settings component",
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
