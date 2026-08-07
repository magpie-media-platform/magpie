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

namespace Magpie.Extensions.OpenLibrary;

internal sealed class AdminUiProvider :
    IUIExtensionProvider,
    IAssetProvider,
    IExtensionSettingsSchemaProvider
{
    private const string RuntimeAssetPath = "metadata-settings.js";
    private const string RuntimeResourceName = "Magpie.Extensions.OpenLibrary.Assets.metadata-settings.js";
    private const string TemplateAssetPath = "metadata-settings.html";
    private const string TemplateResourceName = "Magpie.Extensions.OpenLibrary.Assets.metadata-settings.html";
    private const string AssetPath = "openlibrary-settings.js";
    private const string ResourceName = "Magpie.Extensions.OpenLibrary.Assets.openlibrary-settings.js";

    private static readonly Guid RouteUIExtensionId = new("AC11D85A-1D0B-4291-A6A9-1E707AC59EE7");
    private static readonly Guid NavUIExtensionId = new("A28E41B5-A6AA-4C2C-8959-7370EEFCACAE");
    private static readonly Guid RuntimeAssetId = new("EFDE462C-5F59-48C3-9910-1B0ED56D2B67");
    private static readonly Guid AssetId = new("73A7FBF0-A121-4C03-8903-66B2977DA8E4");

    private static readonly ExtensionSettingsGroupDefinition SettingsGroupDefinition = new()
    {
        ExtensionId = Constants.ExtensionId,
        Name = ProviderSettings.GroupName,
        SettingsSchema =
        [
            new SettingSchemaDefinition
            {
                Key = ProviderSettings.ContactKey,
                Type = DataType.String,
                DisplayName = "Contact",
                Description = "Contact email or phone number used to identify requests to Open Library.",
                Placeholder = "you@example.com"
            }
        ]
    };

    public IEnumerable<UIExtension> GetUIExtensions()
    {
        return
        [
            new UIExtension
            {
                Id = RouteUIExtensionId,
                ExtensionId = Constants.ExtensionId,
                Name = "Open Library Settings",
                Slot = "admin.route.openlibrary",
                Modifier = nameof(UISlotModifier.After),
                TagName = "magpie-openlibrary-settings",
                Priority = 100
            },
            new UIExtension
            {
                Id = NavUIExtensionId,
                ExtensionId = Constants.ExtensionId,
                Name = "Open Library",
                Slot = "nav.admin",
                Modifier = nameof(UISlotModifier.After),
                TagName = "magpie-nav-item",
                Priority = 100,
                Attributes = new Dictionary<string, string>
                {
                    ["label"] = "Open Library",
                    ["href"] = "/admin/ext/openlibrary",
                    ["icon"] = "book"
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
                Name = "Open Library settings component",
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
            var runtimeStream = GetType().Assembly.GetManifestResourceStream(RuntimeResourceName);
            return Task.FromResult<Stream?>(runtimeStream);
        }

        if (string.Equals(relativePath, TemplateAssetPath, StringComparison.Ordinal))
        {
            var templateStream = GetType().Assembly.GetManifestResourceStream(TemplateResourceName);
            return Task.FromResult<Stream?>(templateStream);
        }

        if (!string.Equals(relativePath, AssetPath, StringComparison.Ordinal))
        {
            return Task.FromResult<Stream?>(null);
        }

        var stream = GetType().Assembly.GetManifestResourceStream(ResourceName);
        return Task.FromResult<Stream?>(stream);
    }
}
