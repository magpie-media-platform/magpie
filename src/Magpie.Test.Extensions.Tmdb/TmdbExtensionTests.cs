using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb;
using Magpie.Models.Enums;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tmdb;

[TestFixture]
public class TmdbExtensionTests
{
    [Test]
    public void Identity_IsStable_WhenExtensionIsConstructed()
    {
        // Arrange

        // Act
        var extension = new TmdbExtension();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                extension.Id,
                Is.EqualTo(new Guid("FE120126-EFD7-4DB1-A3A1-8B8A881E013A")));
            Assert.That(extension.Name, Is.EqualTo("TMDb"));
            Assert.That(extension.Version, Is.EqualTo(new Version(1, 0, 0)));
            Assert.That(extension.TargetABI, Is.EqualTo("[1.0.0,)"));
        }
    }

    [Test]
    public async Task GetUIExtensions_DeclareRouteNavAndAssets_WhenAdminUiProviderIsQueried()
    {
        // Arrange
        var adminUi = new AdminUiProvider();
        var expectedExtensionId = new Guid("FE120126-EFD7-4DB1-A3A1-8B8A881E013A");

        // Act
        var uiExtensions = adminUi.GetUIExtensions().ToArray();
        var routeExtension = uiExtensions.Single(ext => ext.Slot == "admin.route.tmdb");
        var navExtension = uiExtensions.Single(ext => ext.Slot == "nav.admin");
        var assets = adminUi.ListAssets().OrderBy(asset => asset.Priority).ToArray();
        var runtimeAsset = assets.Single(asset => asset.RelativePath == "metadata-settings.js");
        var asset = assets.Single(asset => asset.RelativePath == "tmdb-settings.js");
        var runtimeStream = await adminUi.GetAssetStreamAsync(runtimeAsset.RelativePath!).ConfigureAwait(false);
        var templateStream = await adminUi.GetAssetStreamAsync("metadata-settings.html").ConfigureAwait(false);
        var stream = await adminUi.GetAssetStreamAsync(asset.RelativePath!).ConfigureAwait(false);
        try
        {
            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(uiExtensions, Has.Length.EqualTo(2));
                Assert.That(uiExtensions.Select(ext => ext.Slot), Does.Not.Contain("admin.extensions.settings"));
                Assert.That(routeExtension.ExtensionId, Is.EqualTo(expectedExtensionId));
                Assert.That(routeExtension.Modifier, Is.EqualTo("After"));
                Assert.That(routeExtension.TagName, Is.EqualTo("magpie-tmdb-settings"));
                Assert.That(navExtension.ExtensionId, Is.EqualTo(expectedExtensionId));
                Assert.That(navExtension.Modifier, Is.EqualTo("After"));
                Assert.That(navExtension.TagName, Is.EqualTo("magpie-nav-item"));
                Assert.That(navExtension.Attributes, Is.Not.Null);
                Assert.That(navExtension.Attributes!["href"], Is.EqualTo("/admin/ext/tmdb"));
                Assert.That(runtimeAsset.ExtensionId, Is.EqualTo(expectedExtensionId));
                Assert.That(runtimeAsset.Kind, Is.EqualTo(AssetKind.Script));
                Assert.That(asset.ExtensionId, Is.EqualTo(expectedExtensionId));
                Assert.That(asset.Kind, Is.EqualTo(AssetKind.Script));
                Assert.That(runtimeStream, Is.Not.Null);
                Assert.That(templateStream, Is.Not.Null);
                Assert.That(stream, Is.Not.Null);
            }
        }
        finally
        {
            if (runtimeStream is not null)
            {
                await runtimeStream.DisposeAsync().ConfigureAwait(false);
            }

            if (stream is not null)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }

            if (templateStream is not null)
            {
                await templateStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
