using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.UI;
using Magpie.Sdk.Configuration;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Notifications;
using Magpie.Sdk.UI;

using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Extensions.IntegrationTestLifecycle;

/// <summary>
/// The integration-test extension for the package lifecycle journey.
/// </summary>
public sealed class LifecycleExtension :
    IExtension,
    IAssetProvider,
    IExtensionSettingsSchemaProvider,
    INotificationProvider,
    IUIExtensionProvider
{
    private const string AssetPath = "lifecycle.js";
    private const string AssetResourceName =
        "Magpie.Extensions.IntegrationTestLifecycle.Assets.lifecycle.js";
    private const string AssetVersionToken = "__LIFECYCLE_VERSION__";
    private static readonly Guid ExtensionId = new("8A7FEF37-6DD6-4BB5-A2AE-69C78D14E24C");

    /// <inheritdoc />
    public Guid Id => ExtensionId;

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public string Name => "Lifecycle fixture";

    /// <inheritdoc />
    public string? TargetABI => "[1.0.0,)";

    /// <inheritdoc />
    public string ProviderType => "lifecycle-notification";

    /// <inheritdoc />
    public IReadOnlyList<SettingSchemaDefinition> SettingsSchema { get; } =
    [
        new SettingSchemaDefinition
        {
            Key = "endpoint",
            Type = DataType.String,
            DisplayName = "Endpoint"
        }
    ];

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddSingleton<IAssetProvider>(this);
        services.AddSingleton<IExtensionSettingsSchemaProvider>(this);
        services.AddSingleton<INotificationProvider>(this);
        services.AddSingleton<IUIExtensionProvider>(this);
    }

    /// <inheritdoc />
    public IEnumerable<ExtensionSettingsGroupDefinition> GetSettingsGroups()
    {
        return
        [
            new ExtensionSettingsGroupDefinition
            {
                ExtensionId = ExtensionId,
                Name = "lifecycle",
                SettingsSchema = SettingsSchema
            }
        ];
    }

    /// <inheritdoc />
    public IEnumerable<UIExtension> GetUIExtensions()
    {
        return
        [
            new UIExtension
            {
                Id = new Guid("D2408461-D9B4-4566-97CE-E9DB31C888EF"),
                ExtensionId = ExtensionId,
                Name = "Lifecycle fixture settings",
                Slot = "admin.route.lifecycle",
                TagName = "magpie-lifecycle-settings",
                Priority = 100
            },
            new UIExtension
            {
                Id = new Guid("5760DF95-6803-40C2-B39D-A18F274BB7CF"),
                ExtensionId = ExtensionId,
                Name = "Lifecycle fixture page",
                Slot = "route.lifecycle",
                TagName = "magpie-lifecycle-page",
                Priority = 100
            },
            new UIExtension
            {
                Id = new Guid("809992F7-CC98-4B8B-BAAB-96A834801EB9"),
                ExtensionId = ExtensionId,
                Name = "Lifecycle fixture",
                Slot = "nav.admin",
                TagName = "magpie-nav-item",
                Priority = 100,
                Attributes = new Dictionary<string, string>
                {
                    ["label"] = "Lifecycle fixture",
                    ["href"] = "/admin/ext/lifecycle",
                    ["icon"] = "puzzle-piece"
                }
            },
            new UIExtension
            {
                Id = new Guid("08099E83-42EA-48CD-8866-9238D050B068"),
                ExtensionId = ExtensionId,
                Name = "Lifecycle fixture",
                Slot = "nav.main",
                TagName = "magpie-nav-item",
                Priority = 100,
                Attributes = new Dictionary<string, string>
                {
                    ["label"] = "Lifecycle fixture",
                    ["href"] = "/ext/lifecycle",
                    ["icon"] = "puzzle-piece"
                }
            }
        ];
    }

    /// <inheritdoc />
    public IEnumerable<Asset> ListAssets()
    {
        return
        [
            new Asset
            {
                Id = new Guid("3F879EDF-BF6C-455B-85CA-0CF8E20AE7DC"),
                ExtensionId = ExtensionId,
                Name = "Lifecycle fixture components",
                Kind = AssetKind.Script,
                RelativePath = AssetPath,
                Priority = 100
            }
        ];
    }

    /// <inheritdoc />
    public Task<Stream?> GetAssetStreamAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(relativePath, AssetPath, StringComparison.Ordinal))
        {
            return Task.FromResult<Stream?>(null);
        }

        using var stream = GetType().Assembly.GetManifestResourceStream(AssetResourceName);
        if (stream is null)
        {
            return Task.FromResult<Stream?>(null);
        }

        using var reader = new StreamReader(stream);
        var version = GetType().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? Version.ToString();
        var script = reader.ReadToEnd().Replace(AssetVersionToken, version, StringComparison.Ordinal);
        return Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes(script), writable: false));
    }

    /// <inheritdoc />
    public Task SendAsync(
        NotificationRule rule,
        MagpieEvent magpieEvent,
        CancellationToken cancellationToken = default)
    {
        if (rule is null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        if (magpieEvent is null)
        {
            throw new ArgumentNullException(nameof(magpieEvent));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
