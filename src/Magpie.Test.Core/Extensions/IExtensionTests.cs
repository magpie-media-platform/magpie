using System;

using Magpie.Sdk.Extensions;

using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

namespace Magpie.Test.Core.Extensions;

[TestFixture]
public sealed class IExtensionTests
{
    [Test]
    public void Version_ReturnsTheThreePartAssemblyVersion_WhenTheExtensionDoesNotDeclareOne()
    {
        // Arrange
        IExtension extension = new TestExtension();

        // Act
        var version = extension.Version;

        // Assert
        Assert.That(version, Is.EqualTo(new Version(3, 4, 5)));
    }

    private sealed class TestExtension : IExtension
    {
        public Guid Id => Guid.Empty;

        public string Name => "Test";

        public void RegisterServices(IServiceCollection services)
        {
        }
    }
}
