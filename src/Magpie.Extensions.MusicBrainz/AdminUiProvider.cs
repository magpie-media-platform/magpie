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

namespace Magpie.Extensions.MusicBrainz;

internal sealed class AdminUiProvider :
    IUIExtensionProvider,
    IAssetProvider,
    IExtensionSettingsSchemaProvider
{
    private static readonly Guid RouteUIExtensionId = new("F70B818D-6774-498F-8EE1-ED4A8274AD0C");
    private static readonly Guid NavUIExtensionId = new("490BA688-7F6A-4C46-8E53-86F2DEED5C10");
    private static readonly Guid RuntimeAssetId = new("48151382-8796-48C2-806E-041BC4510055");
    private static readonly Guid AssetId = new("C844F1DF-B5A1-4997-84A0-E6A72F903BA7");

    private static readonly ExtensionSettingsGroupDefinition SettingsGroupDefinition = new()
    {
        ExtensionId = Constants.ExtensionId,
        Name = Settings.GroupName,
        SettingsSchema =
        [new SettingSchemaDefinition { Key = Settings.ContactKey, Type = DataType.String }]
    };

    private const string RuntimeAssetPath = "metadata-settings.js";
    private const string RuntimeResourceName = "Magpie.Extensions.MusicBrainz.Assets.metadata-settings.js";
    private const string TemplateAssetPath = "metadata-settings.html";
    private const string TemplateResourceName = "Magpie.Extensions.MusicBrainz.Assets.metadata-settings.html";
    private const string AssetPath = "musicbrainz-settings.js";
    private const string ResourceName = "Magpie.Extensions.MusicBrainz.Assets.musicbrainz-settings.js";

    public IEnumerable<UIExtension> GetUIExtensions()
    {
        return
        [
            new UIExtension
            {
                Id = RouteUIExtensionId,
                ExtensionId = Constants.ExtensionId,
                Name = "MusicBrainz Settings",
                Slot = "admin.route.musicbrainz",
                Modifier = nameof(UISlotModifier.After),
                TagName = "magpie-musicbrainz-settings",
                Priority = 100
            },
            new UIExtension
            {
                Id = NavUIExtensionId,
                ExtensionId = Constants.ExtensionId,
                Name = "MusicBrainz",
                Slot = "nav.admin",
                Modifier = nameof(UISlotModifier.After),
                TagName = "magpie-nav-item",
                Priority = 100,
                Attributes = new Dictionary<string, string>
                {
                    ["label"] = "MusicBrainz",
                    ["href"] = "/admin/ext/musicbrainz",
                    ["icon"] = "music-note"
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
                Name = "MusicBrainz settings component",
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
