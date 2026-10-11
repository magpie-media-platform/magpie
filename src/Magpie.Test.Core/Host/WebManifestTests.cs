using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.App.Hosting;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.FileProviders;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Host;

[TestFixture]
public sealed class WebManifestTests
{
    private Mock<IWebHostEnvironment> _environment = null!;
    private Mock<IFileInfo> _manifestFile = null!;
    private Mock<ISettingsService> _settingsService = null!;
    private HttpResponse _response = null!;

    [SetUp]
    public void SetUp()
    {
        _manifestFile = new Mock<IFileInfo>();
        _manifestFile.SetupGet(file => file.Exists).Returns(true);
        _manifestFile
            .Setup(file => file.CreateReadStream())
            .Returns(() => new MemoryStream(Encoding.UTF8.GetBytes(
                "{\"name\":\"Magpie\",\"short_name\":\"Magpie\",\"start_url\":\"./\"}")));
        var fileProvider = new Mock<IFileProvider>();
        fileProvider.Setup(provider => provider.GetFileInfo("manifest.webmanifest")).Returns(_manifestFile.Object);
        _environment = new Mock<IWebHostEnvironment>();
        _environment.SetupGet(environment => environment.WebRootFileProvider).Returns(fileProvider.Object);
        _settingsService = new Mock<ISettingsService>();
        _response = new DefaultHttpContext().Response;
    }

    [Test]
    public async Task GetAsync_ReturnsManifestWithInstanceName_WhenManifestFileExists()
    {
        // Arrange
        _settingsService
            .Setup(service => service.GetInstanceNameAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("Home Lab");

        // Act
        var result = await WebManifest.GetAsync(
            _environment.Object,
            _settingsService.Object,
            _response,
            CancellationToken.None).ConfigureAwait(false);

        // Assert
        var content = (ContentHttpResult)result;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                content.ResponseContent,
                Is.EqualTo("{\"name\":\"Home Lab\",\"short_name\":\"Home Lab\",\"start_url\":\"./\"}"));
            Assert.That(content.ContentType, Is.EqualTo("application/manifest+json"));
            Assert.That(_response.Headers.CacheControl.ToString(), Is.EqualTo("no-cache"));
        }
    }

    [Test]
    public async Task GetAsync_ReturnsNotFound_WhenManifestFileIsMissing()
    {
        // Arrange
        _manifestFile.SetupGet(file => file.Exists).Returns(false);

        // Act
        var result = await WebManifest.GetAsync(
            _environment.Object,
            _settingsService.Object,
            _response,
            CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.TypeOf<NotFound>());
    }
}
