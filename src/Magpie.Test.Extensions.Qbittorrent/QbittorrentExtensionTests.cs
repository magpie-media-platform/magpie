using System;

using Magpie.Extensions.Qbittorrent;
using Magpie.Sdk.Integrations;

using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Qbittorrent;

[TestFixture]
public class QbittorrentExtensionTests
{
    private static readonly Guid ExpectedExtensionId = new("F9238BE4-1471-4C6E-859F-D26D472577EC");

    [Test]
    public void QbittorrentExtension_Identity_WhenCreated()
    {
        // Arrange

        // Act
        var extension = new QbittorrentExtension();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(extension.Id, Is.EqualTo(ExpectedExtensionId));
            Assert.That(extension.Name, Is.EqualTo("qBittorrent"));
            Assert.That(extension.Version, Is.EqualTo(new Version(1, 0, 0)));
            Assert.That(extension.TargetABI, Is.EqualTo("[1.0.0,)"));
        }
    }

    [Test]
    public void RegisterServices_ClientAndIntegration_WhenExtensionRegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var extension = new QbittorrentExtension();

        // Act
        extension.RegisterServices(services);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IQbittorrentClient>();
        var integration = provider.GetRequiredService<IDownloadClientIntegration>();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(client, Is.InstanceOf<QbittorrentClient>());
            Assert.That(integration, Is.InstanceOf<QbittorrentDownloadClientIntegration>());
        }
    }
}
