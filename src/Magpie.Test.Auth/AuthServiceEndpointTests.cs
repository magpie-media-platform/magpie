using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Api;
using Magpie.Auth;
using Magpie.Models.Enums;
using Magpie.Sdk.Auth;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Auth;

[TestFixture]
public class AuthServiceEndpointTests
{
    private WebApplication _app = null!;

    [OneTimeSetUp]
    public void SetUp()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddMagpieAuth(builder.Configuration);
        builder.Services.AddSingleton(Mock.Of<ISubjectKindRegistry>());
        new MagpieCoreApiExtension().RegisterServices(builder.Services);
        _app = builder.Build();
        _app.MapMagpieEndpoints();
    }

    [OneTimeTearDown]
    public async Task TearDown()
    {
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    [TestCase("content.add", "/api/content/from-external-id", "POST", Role.Viewer, Role.Requester, false)]
    [TestCase("content.add", "/api/content/from-external-id", "POST", Role.Requester, Role.Requester, true)]
    [TestCase("quality.view", "/api/quality-tiers", "GET", Role.Viewer, Role.Viewer, true)]
    [TestCase("system.view", "/api/system", "GET", Role.Viewer, Role.Viewer, true)]
    [TestCase("settings.view", "/api/settings/groups", "GET", Role.Requester, Role.Manager, false)]
    [TestCase("settings.view", "/api/settings/groups", "GET", Role.Manager, Role.Manager, true)]
    [TestCase("filters.manage", "/api/content-filters", "POST", Role.Viewer, Role.Viewer, true)]
    [TestCase("filters.manage", "/api/content-filters/{id:guid}", "PATCH", Role.Viewer, Role.Viewer, true)]
    [TestCase("filters.manage", "/api/content-filters/{id:guid}", "DELETE", Role.Viewer, Role.Viewer, true)]
    [TestCase("jobs.view", "/api/jobs", "GET", Role.Viewer, Role.Viewer, true)]
    [TestCase("files.browse", "/api/system/files", "GET", Role.Requester, Role.Manager, false)]
    [TestCase("files.browse", "/api/system/files", "GET", Role.Manager, Role.Manager, true)]
    [TestCase("content.view", "/api/content", "GET", Role.Viewer, Role.Viewer, true)]
    [TestCase("content.edit", "/api/content/{id:guid}", "PATCH", Role.Requester, Role.Manager, false)]
    [TestCase("content.edit", "/api/content/{id:guid}", "PATCH", Role.Manager, Role.Manager, true)]
    [TestCase("content.delete", "/api/content/{id:guid}", "DELETE", Role.Requester, Role.Manager, false)]
    [TestCase("content.delete", "/api/content/{id:guid}", "DELETE", Role.Manager, Role.Manager, true)]
    [TestCase("content.request", "/api/interactive-search/{contentHierarchyItemId:guid}/grab", "POST",
        Role.Viewer, Role.Requester, false)]
    [TestCase("content.request", "/api/interactive-search/{contentHierarchyItemId:guid}/grab", "POST",
        Role.Requester, Role.Requester, true)]
    [TestCase("subjects.view", "/api/subjects", "GET", Role.Viewer, Role.Viewer, true)]
    [TestCase("subjects.edit", "/api/subjects/{id:guid}", "PATCH", Role.Viewer, Role.Requester, false)]
    [TestCase("subjects.edit", "/api/subjects/{id:guid}", "PATCH", Role.Requester, Role.Requester, true)]
    [TestCase("downloads.view", "/api/downloads", "GET", Role.Viewer, Role.Viewer, true)]
    [TestCase("downloads.control", "/api/downloads/{id:guid}/pause", "POST", Role.Viewer, Role.Requester, false)]
    [TestCase("downloads.control", "/api/downloads/{id:guid}/pause", "POST",
        Role.Requester, Role.Requester, true)]
    [TestCase("downloads.manage", "/api/downloads/{id:guid}", "DELETE", Role.Requester, Role.Manager, false)]
    [TestCase("downloads.manage", "/api/downloads/{id:guid}", "DELETE", Role.Manager, Role.Manager, true)]
    [TestCase("indexers.manage", "/api/indexers", "POST", Role.Manager, Role.Admin, false)]
    [TestCase("indexers.manage", "/api/indexers", "POST", Role.Admin, Role.Admin, true)]
    [TestCase("clients.manage", "/api/download-clients", "POST", Role.Manager, Role.Admin, false)]
    [TestCase("clients.manage", "/api/download-clients", "POST", Role.Admin, Role.Admin, true)]
    [TestCase("servers.manage", "/api/media-servers", "POST", Role.Manager, Role.Admin, false)]
    [TestCase("servers.manage", "/api/media-servers", "POST", Role.Admin, Role.Admin, true)]
    [TestCase("folders.manage", "/api/root-folders", "POST", Role.Manager, Role.Admin, false)]
    [TestCase("folders.manage", "/api/root-folders", "POST", Role.Admin, Role.Admin, true)]
    [TestCase("jobs.run", "/api/jobs/enqueue", "POST", Role.Viewer, Role.Requester, false)]
    [TestCase("jobs.run", "/api/jobs/enqueue", "POST", Role.Requester, Role.Requester, true)]
    [TestCase("jobs.schedule", "/api/schedule-triggers", "POST", Role.Viewer, Role.Requester, false)]
    [TestCase("jobs.schedule", "/api/schedule-triggers", "POST", Role.Requester, Role.Requester, true)]
    [TestCase("jobs.manage", "/api/jobs/{id:guid}/cancel", "POST", Role.Requester, Role.Manager, false)]
    [TestCase("jobs.manage", "/api/jobs/{id:guid}/cancel", "POST", Role.Manager, Role.Manager, true)]
    [TestCase("wishlists.view", "/api/wishlists", "GET", Role.Viewer, Role.Viewer, true)]
    [TestCase("wishlists.own", "/api/wishlists", "POST", Role.Viewer, Role.Requester, false)]
    [TestCase("wishlists.own", "/api/wishlists", "POST", Role.Requester, Role.Requester, true)]
    [TestCase("wishlists.manage", "/api/wishlists/{id:guid}", "DELETE", Role.Viewer, Role.Requester, false)]
    [TestCase("wishlists.manage", "/api/wishlists/{id:guid}", "DELETE", Role.Requester, Role.Requester, true)]
    [TestCase("quality.manage", "/api/quality-tiers", "POST", Role.Manager, Role.Admin, false)]
    [TestCase("quality.manage", "/api/quality-tiers", "POST", Role.Admin, Role.Admin, true)]
    [TestCase("tags.view", "/api/tags", "GET", Role.Viewer, Role.Viewer, true)]
    [TestCase("filters.view", "/api/content-filters", "GET", Role.Viewer, Role.Viewer, true)]
    [TestCase("filters.own", "/api/content-filters", "POST", Role.Viewer, Role.Viewer, true)]
    [TestCase("activity.view", "/api/audit-history", "GET", Role.Viewer, Role.Viewer, true)]
    [TestCase("settings.edit", "/api/settings/groups/system/{name}", "PATCH", Role.Manager, Role.Admin, false)]
    [TestCase("settings.edit", "/api/settings/groups/system/{name}", "PATCH", Role.Admin, Role.Admin, true)]
    [TestCase("users.manage", "/api/users", "GET", Role.Manager, Role.Admin, false)]
    [TestCase("users.manage", "/api/users", "GET", Role.Admin, Role.Admin, true)]
    [TestCase("extensions.manage", "/api/extensions", "POST", Role.Manager, Role.Admin, false)]
    [TestCase("extensions.manage", "/api/extensions", "POST", Role.Admin, Role.Admin, true)]
    [TestCase("backups.manage", "/api/database/backups", "GET", Role.Manager, Role.Admin, false)]
    [TestCase("backups.manage", "/api/database/backups", "GET", Role.Admin, Role.Admin, true)]
    public async Task GetEffectivePermissions_ReturnsScopedGrants_WhenTheCallerHasAnEndpointRole(
        string permission, string route, string method, Role callerRole, Role requiredRole, bool granted)
    {
        // Arrange
        var subject = new AuthService([], Mock.Of<ITokenService>(), Mock.Of<IUserService>(), Mock.Of<IEventBus>(),
            new AccountLockoutOptions(), TimeProvider.System, NullLogger<AuthService>.Instance);
        var endpoint = ((IEndpointRouteBuilder)_app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Single(value => value.RoutePattern.RawText?.TrimEnd('/') == route &&
                value.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(method));
        var policy = await AuthorizationPolicy.CombineAsync(
            _app.Services.GetRequiredService<IAuthorizationPolicyProvider>(),
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()).ConfigureAwait(false);
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, callerRole.ToString())], "test"));

        // Act
        var result = subject.GetEffectivePermissions(callerRole);
        var authorization = await _app.Services.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(user, null, policy!).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Permissions[permission].RequiredRole, Is.EqualTo(requiredRole));
            Assert.That(result.Permissions[permission].Granted, Is.EqualTo(granted));
            Assert.That(authorization.Succeeded, Is.EqualTo(granted));
        }
    }
}
