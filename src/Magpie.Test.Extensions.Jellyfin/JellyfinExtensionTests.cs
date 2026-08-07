using System;

using Magpie.Extensions.Jellyfin;
using Magpie.Sdk.Integrations;

using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Jellyfin;

[TestFixture]
public class JellyfinExtensionTests
{
    private static readonly Guid ExpectedExtensionId = new("6701BA12-86A7-4746-8B35-144B5B167B8D");

    [Test]
    public void Identity_ReturnsStableValue_WhenRead()
    {
        // Arrange

        // Act
        var extension = new JellyfinExtension();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(extension.Id, Is.EqualTo(ExpectedExtensionId));
            Assert.That(extension.Name, Is.EqualTo("Jellyfin"));
            Assert.That(extension.Version, Is.EqualTo(new Version(1, 0, 0)));
            Assert.That(extension.TargetABI, Is.EqualTo("[1.0.0,)"));
        }
    }

    [Test]
    public void Register_ResolvesIntegration_WhenScopesAreValidated()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        new JellyfinExtension().RegisterServices(services);

        // Act
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var subject = provider.GetRequiredService<IMediaServerIntegration>();

        // Assert
        Assert.That(subject, Is.InstanceOf<JellyfinMediaServerIntegration>());
    }
}
