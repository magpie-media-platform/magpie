using System.IO;

using Magpie.App.Extensions;

using NUnit.Framework;

namespace Magpie.Test.App.Extensions;

[TestFixture]
public class ExtensionPathResolverTests
{
    [Test]
    public void Resolve_DefaultsToContentRootSubdirectory_WhenConfiguredPathIsNull()
    {
        var contentRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "magpie-resolver-default"));

        var result = ExtensionPathResolver.Resolve(null, contentRoot);

        Assert.That(
            result,
            Is.EqualTo(Path.GetFullPath(Path.Combine(contentRoot, ExtensionPathResolver.DefaultDirectoryName))));
    }

    [Test]
    public void Resolve_DefaultsToContentRootSubdirectory_WhenConfiguredPathIsEmpty()
    {
        var contentRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "magpie-resolver-empty"));

        var result = ExtensionPathResolver.Resolve("   ", contentRoot);

        Assert.That(
            result,
            Is.EqualTo(Path.GetFullPath(Path.Combine(contentRoot, ExtensionPathResolver.DefaultDirectoryName))));
    }

    [Test]
    public void Resolve_ReturnsConfiguredPath_WhenAbsolute()
    {
        var contentRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "magpie-resolver-abs"));
        var configured = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "magpie-extensions-abs"));

        var result = ExtensionPathResolver.Resolve(configured, contentRoot);

        Assert.That(result, Is.EqualTo(configured));
    }

    [Test]
    public void Resolve_RootsRelativePathAtContentRoot()
    {
        var contentRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "magpie-resolver-rel"));

        var result = ExtensionPathResolver.Resolve("plugins", contentRoot);

        Assert.That(result, Is.EqualTo(Path.GetFullPath(Path.Combine(contentRoot, "plugins"))));
    }
}
