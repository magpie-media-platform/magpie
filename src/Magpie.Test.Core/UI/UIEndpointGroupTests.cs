using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Api;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.UI;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;
using Magpie.Sdk.UI;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.UI;

[TestFixture]
public sealed class UIEndpointGroupTests
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Mock<ISettingsService> _settings = new();
    private ServiceProvider _services = null!;
    private RouteEndpoint _themesEndpoint = null!;
    private RouteEndpoint _stylesheetEndpoint = null!;
    private MemoryStream _responseBody = null!;
    private DefaultHttpContext _context = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var failingProvider = new Mock<IThemeProvider>();
        failingProvider.Setup(provider => provider.GetThemes()).Throws<InvalidOperationException>();
        var services = new ServiceCollection();
        new MagpieCoreApiExtension().RegisterServices(services);
        services.AddRouting();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(_settings.Object);
        services.AddSingleton(failingProvider.Object);
        services.AddSingleton<IThemeProvider, TestThemeProvider>();
        _services = services.BuildServiceProvider();

        var routes = new Mock<IEndpointRouteBuilder>();
        var dataSources = new List<EndpointDataSource>();
        routes.SetupGet(builder => builder.ServiceProvider).Returns(_services);
        routes.SetupGet(builder => builder.DataSources).Returns(dataSources);
        _services.GetServices<IApiEndpointGroup>().Single(group => group.Name == "ui").MapEndpoints(routes.Object);
        var endpoints = dataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        _themesEndpoint = endpoints.Single(endpoint => endpoint.RoutePattern.RawText == "themes");
        _stylesheetEndpoint = endpoints.Single(endpoint => endpoint.RoutePattern.RawText == "themes/stylesheet");
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _services.Dispose();
    }

    [SetUp]
    public void SetUp()
    {
        _settings.Reset();
        _responseBody = new MemoryStream();
        _context = new DefaultHttpContext { RequestServices = _services };
        _context.Response.Body = _responseBody;
    }

    [TearDown]
    public void TearDown()
    {
        _responseBody.Dispose();
    }

    [Test]
    public void MapEndpoints_AllowsAnonymousThemeReads_WhenEndpointsAreMapped()
    {
        // Arrange

        // Act
        var themesAccess = _themesEndpoint.Metadata.GetMetadata<IAllowAnonymous>();
        var stylesheetAccess = _stylesheetEndpoint.Metadata.GetMetadata<IAllowAnonymous>();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(themesAccess, Is.Not.Null);
            Assert.That(stylesheetAccess, Is.Not.Null);
        }
    }

    [Test]
    public async Task GetThemesAsync_ListsEachValidThemeOnce_WhenProvidersSupplyInvalidDuplicateOrNoThemes()
    {
        // Arrange

        // Act
        await _themesEndpoint.RequestDelegate!(_context).ConfigureAwait(false);
        var value = JsonSerializer.Deserialize<ThemeCatalogResponse>(_responseBody.ToArray(), _jsonOptions)!;

        // Assert
        Assert.That(value.Themes.Select(theme => theme.Id), Is.EqualTo(
        [
            "Magpie.Api/Noir/Dark",
            "Magpie.Api/Tokyo Night/Dark",
            "Magpie.Api/Slate/Dark",
            "Magpie.Api/Ember/Dark",
            "Magpie.Api/Evergreen/Dark",
            "Magpie.Api/Frost/Light",
            "Magpie.Api/Parchment/Light",
            "Magpie.Api/Seafoam/Light",
            "Magpie.Api/Banana Stand/Light",
            "Magpie.Api/Bubblegum/Light",
            "Magpie.Test.Core/Ocean/Dark",
            "Magpie.Test.Core/Ocean/Light"
        ]));
    }

    [Test]
    public async Task GetThemesAsync_ReturnsBuiltInDefaults_WhenSettingsAreMissing()
    {
        // Arrange

        // Act
        await _themesEndpoint.RequestDelegate!(_context).ConfigureAwait(false);
        var value = JsonSerializer.Deserialize<ThemeCatalogResponse>(_responseBody.ToArray(), _jsonOptions)!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(value.Defaults.Mode, Is.EqualTo(ThemeMode.Dark));
            Assert.That(value.Defaults.DarkTheme, Is.EqualTo("Magpie.Api/Tokyo Night/Dark"));
            Assert.That(value.Defaults.LightTheme, Is.EqualTo("Magpie.Api/Frost/Light"));
        }
    }

    [TestCase("System", "Magpie.Api/Ember/Dark", "Magpie.Api/Bubblegum/Light", ThemeMode.System)]
    [TestCase("invalid", "removed", "Magpie.Api/Tokyo Night/Dark", ThemeMode.Dark)]
    [TestCase("Light", "removed-dark", "removed-light", ThemeMode.Light)]
    [TestCase("999", "removed-dark", "removed-light", ThemeMode.Dark)]
    public async Task GetThemesAsync_ReturnsStoredThemeSelections_WhenSettingsExist(
        string mode,
        string dark,
        string light,
        ThemeMode expectedMode)
    {
        // Arrange
        _settings.Setup(service => service.GetSystemGroupAsync("theme", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SettingsGroupDetail
            {
                Group = new SettingsGroup
                {
                    Name = "theme",
                    Settings =
                    {
                        new Setting { Key = "mode", Value = mode },
                        new Setting { Key = "darkTheme", Value = dark },
                        new Setting { Key = "lightTheme", Value = light }
                    }
                }
            });

        // Act
        await _themesEndpoint.RequestDelegate!(_context).ConfigureAwait(false);
        var value = JsonSerializer.Deserialize<ThemeCatalogResponse>(_responseBody.ToArray(), _jsonOptions)!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(value.Defaults.Mode, Is.EqualTo(expectedMode));
            Assert.That(value.Defaults.DarkTheme, Is.EqualTo(dark));
            Assert.That(value.Defaults.LightTheme, Is.EqualTo(light));
        }
    }

    [Test]
    public async Task GetThemeStylesheet_ReturnsEmbeddedCss_WhenThemeIsListed()
    {
        // Arrange
        _context.Request.QueryString = QueryString.Create("id", "Magpie.Test.Core/Ocean/Dark");

        // Act
        await _stylesheetEndpoint.RequestDelegate!(_context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_context.Response.StatusCode, Is.EqualTo(200));
            Assert.That(_context.Response.ContentType, Is.EqualTo("text/css"));
            Assert.That(Encoding.UTF8.GetString(_responseBody.ToArray()), Does.Contain("color-scheme: dark"));
        }
    }

    [TestCase("test-theme.css")]
    [TestCase("../secret.json")]
    [TestCase("Magpie.Test.Core/Missing/Dark")]
    public async Task GetThemeStylesheet_ReturnsNotFound_WhenThemeIsNotListed(string id)
    {
        // Arrange
        _context.Request.QueryString = QueryString.Create("id", id);

        // Act
        await _stylesheetEndpoint.RequestDelegate!(_context).ConfigureAwait(false);

        // Assert
        Assert.That(_context.Response.StatusCode, Is.EqualTo(404));
    }

    private sealed class TestThemeProvider : IThemeProvider
    {
        public IEnumerable<ThemeDefinition> GetThemes() =>
        [
            new() { Name = "Ocean", FileName = "test-theme.css", Variant = ThemeVariant.Dark },
            new() { Name = "Ocean", FileName = "test-theme.css", Variant = ThemeVariant.Dark },
            new() { Name = "Ocean", FileName = "test-theme.css", Variant = ThemeVariant.Light },
            new() { Name = "", FileName = "test-theme.css", Variant = ThemeVariant.Dark },
            new() { Name = "Missing", FileName = "missing.css", Variant = ThemeVariant.Dark },
            new() { Name = "Secret", FileName = "../secret.json", Variant = ThemeVariant.Dark },
            new() { Name = "Unknown", FileName = "test-theme.css", Variant = (ThemeVariant)999 }
        ];
    }
}
