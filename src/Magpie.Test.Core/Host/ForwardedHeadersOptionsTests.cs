using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

using NUnit.Framework;

namespace Magpie.Test.Core.Host;

[TestFixture]
public sealed class ForwardedHeadersOptionsTests
{
    [Test]
    public void BuildForwardedHeadersOptions_TrustsListedProxies_WhenAddressAndRangeGiven()
    {
        // Arrange
        string[] proxies = ["10.1.2.3", "10.42.0.0/16"];

        // Act
        var options = MagpieHostBuilderExtensions.BuildForwardedHeadersOptions(proxies);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                options.ForwardedHeaders,
                Is.EqualTo(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto));
            Assert.That(options.KnownProxies, Is.EqualTo([IPAddress.Parse("10.1.2.3")]));
            Assert.That(options.KnownIPNetworks, Has.Count.EqualTo(1));
            Assert.That(options.KnownIPNetworks[0].ToString(), Is.EqualTo("10.42.0.0/16"));
        }
    }

    [Test]
    public void BuildForwardedHeadersOptions_Throws_WhenProxyIsNotAnAddress()
    {
        // Arrange
        string[] proxies = ["not-an-address"];

        // Act
        var exception = Assert.Throws<System.InvalidOperationException>(
            () => MagpieHostBuilderExtensions.BuildForwardedHeadersOptions(proxies));

        // Assert
        Assert.That(exception!.Message, Does.Contain("not-an-address"));
    }
}
