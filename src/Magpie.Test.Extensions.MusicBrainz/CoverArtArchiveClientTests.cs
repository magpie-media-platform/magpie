using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Magpie.Extensions.MusicBrainz;
using Magpie.Extensions.MusicBrainz.QueryEngine;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

using RichardSzalay.MockHttp;

namespace Magpie.Test.Extensions.MusicBrainz;

[TestFixture]
public class CoverArtArchiveClientTests
{
    [Test]
    public async Task GetReleaseGroupFrontAsync_Success_WhenHttpsRedirectIsReturned()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        var userAgent = string.Empty;
        http.Expect(
            HttpMethod.Head,
            "https://coverartarchive.org/release-group/550e8400-e29b-41d4-a716-446655440000/front-500")
            .Respond(request =>
            {
                userAgent = request.Headers.UserAgent.ToString();
                var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
                response.Headers.Location = new Uri("https://archive.org/front.jpg");
                return response;
            });
        using var httpClient = http.ToHttpClient();
        httpClient.BaseAddress = new Uri(CoverArtArchiveClient.DefaultBaseUrl);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(CoverArtArchiveClient.HttpClientName)).Returns(httpClient);
        var client = new CoverArtArchiveClient(
            factory.Object,
            NullLogger<CoverArtArchiveClient>.Instance);

        // Act
        var result = await client.GetReleaseGroupFrontAsync(
            "550e8400-e29b-41d4-a716-446655440000",
            500,
            new Settings { Contact = "tests@example.com" },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(FetchOutcome.Success));
            Assert.That(result.Value!.AbsoluteUri, Is.EqualTo("https://archive.org/front.jpg"));
            Assert.That(userAgent, Does.Contain("tests@example.com"));
        }

        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetReleaseGroupFrontAsync_NotFound_WhenServerReturnsNotFound()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Head, "https://coverartarchive.org/release-group/*/front-500")
            .Respond(HttpStatusCode.NotFound);
        using var httpClient = http.ToHttpClient();
        httpClient.BaseAddress = new Uri(CoverArtArchiveClient.DefaultBaseUrl);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(CoverArtArchiveClient.HttpClientName)).Returns(httpClient);
        var client = new CoverArtArchiveClient(
            factory.Object,
            NullLogger<CoverArtArchiveClient>.Instance);

        // Act
        var result = await client.GetReleaseGroupFrontAsync(
            "550e8400-e29b-41d4-a716-446655440000",
            500,
            new Settings(),
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Outcome, Is.EqualTo(FetchOutcome.NotFound));
        http.VerifyNoOutstandingExpectation();
    }

    [Test]
    public async Task GetReleaseGroupFrontAsync_Malformed_WhenRedirectUsesHttp()
    {
        // Arrange
        using var http = new MockHttpMessageHandler();
        http.Expect(HttpMethod.Head, "https://coverartarchive.org/release-group/*/front-500")
            .Respond(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
                response.Headers.Location = new Uri("http://archive.org/front.jpg");
                return response;
            });
        using var httpClient = http.ToHttpClient();
        httpClient.BaseAddress = new Uri(CoverArtArchiveClient.DefaultBaseUrl);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(CoverArtArchiveClient.HttpClientName)).Returns(httpClient);
        var client = new CoverArtArchiveClient(
            factory.Object,
            NullLogger<CoverArtArchiveClient>.Instance);

        // Act
        var result = await client.GetReleaseGroupFrontAsync(
            "550e8400-e29b-41d4-a716-446655440000",
            500,
            new Settings(),
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Outcome, Is.EqualTo(FetchOutcome.Malformed));
        http.VerifyNoOutstandingExpectation();
    }
}
