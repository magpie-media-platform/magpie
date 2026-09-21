using System;
using System.Collections.Generic;
using System.IO;

using Magpie.Core.Extensions;
using Magpie.Models.Data;
using Magpie.Sdk.Extensions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

namespace Magpie.Test.Core.Extensions;

[TestFixture]
public sealed class ExtensionLoaderTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "magpie-extension-loader", Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, true);
    }

    [Test]
    public void LoadExtensions_LeavesTheExtensionDirectoryMovable_WhenTheAssemblyFailsToLoad()
    {
        // Arrange
        var installDirectory = Path.Combine(_root, "install");
        var movedDirectory = Path.Combine(_root, "moved");
        Directory.CreateDirectory(installDirectory);
        var source = typeof(IExtension).Assembly.Location;
        var assemblyPath = Path.Combine(installDirectory, "Magpie.Sdk.dll");
        File.Copy(source, assemblyPath);
        File.Copy(Path.ChangeExtension(source, ".pdb"), Path.Combine(installDirectory, "Magpie.Sdk.pdb"));
        var row = new Extension
        {
            Id = Guid.NewGuid(),
            Name = "Loadable",
            PackageId = "Magpie.Sdk",
            AssemblyName = "Magpie.Sdk",
            Version = "1.0.0",
            FilePath = assemblyPath,
        };
        var failures = new List<Extension>();
        var subject = new ExtensionLoader(NullLogger<ExtensionLoader>.Instance);

        // Act
        subject.LoadExtensions([row], new ServiceCollection(), failures);
        Directory.Move(installDirectory, movedDirectory);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failures, Is.EquivalentTo([row]));
            Assert.That(Directory.Exists(installDirectory), Is.False);
            Assert.That(File.Exists(Path.Combine(movedDirectory, "Magpie.Sdk.dll")), Is.True);
        }
    }
}
