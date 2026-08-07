using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

[MagpieCommandGroup("settings", "settings", "Manage system settings")]
internal sealed class SettingsEndpointGroup : IApiEndpointGroup
{
    public string Name => "settings";

    private static SettingsGroupResponse ToResponse(SettingsGroupDetail detail)
    {
        var group = detail.Group;

        if (!detail.SchemaResolved)
        {
            return new SettingsGroupResponse
            {
                Name = group.Name,
                ExtensionId = group.ExtensionId,
                Settings = [],
                SecretSettings = new Dictionary<string, SecretSettingState>(),
                Schema = detail.Schema,
                SchemaResolved = false,
                CanTest = detail.CanTest
            };
        }

        var secretKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in detail.Schema)
        {
            if (definition.Secret)
            {
                secretKeys.Add(definition.Key);
            }
        }

        var stored = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var visible = new List<Setting>(group.Settings.Count);
        foreach (var setting in group.Settings)
        {
            stored[setting.Key] = setting.Value;
            if (!secretKeys.Contains(setting.Key))
            {
                visible.Add(setting);
            }
        }

        var secretSettings = new Dictionary<string, SecretSettingState>();
        foreach (var key in secretKeys)
        {
            secretSettings[key] = new SecretSettingState
            {
                IsSet = stored.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            };
        }

        return new SettingsGroupResponse
        {
            Name = group.Name,
            ExtensionId = group.ExtensionId,
            Settings = visible,
            SecretSettings = secretSettings,
            Schema = detail.Schema,
            SchemaResolved = true,
            CanTest = detail.CanTest
        };
    }

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("groups", GetSettingsGroupsAsync)
            .WithName(nameof(GetSettingsGroupsAsync))
            .Produces<IReadOnlyList<SettingsGroupResponse>>()
            .RequireManager();

        builder.MapGet("groups/system/{name}", GetSystemSettingsGroupAsync)
            .WithName(nameof(GetSystemSettingsGroupAsync))
            .Produces<SettingsGroupResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapPatch("groups/system/{name}", PatchSystemSettingsGroupAsync)
            .WithName(nameof(PatchSystemSettingsGroupAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapGet("groups/{extensionId:guid}/{name}", GetExtensionSettingsGroupAsync)
            .WithName(nameof(GetExtensionSettingsGroupAsync))
            .Produces<SettingsGroupResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapPatch("groups/{extensionId:guid}/{name}", PatchExtensionSettingsGroupAsync)
            .WithName(nameof(PatchExtensionSettingsGroupAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapPost("groups/{extensionId:guid}/{name}/test", TestSettingsGroupAsync)
            .WithName(nameof(TestSettingsGroupAsync))
            .Produces<ConfigurationTestResult>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();
    }

    [MagpieCommand("list", "GET", "groups", "List settings groups")]
    internal async Task<IResult> GetSettingsGroupsAsync(
        [FromServices] ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        var groups = await settingsService.ListGroupsAsync(cancellationToken).ConfigureAwait(false);

        var wireGroups = new List<SettingsGroupResponse>(groups.Count);
        foreach (var group in groups)
        {
            wireGroups.Add(ToResponse(group));
        }

        return Results.Ok(wireGroups);
    }

    [MagpieCommand("get-system", "GET", "groups/system/{name}", "Show a system settings group")]
    internal async Task<IResult> GetSystemSettingsGroupAsync(
        [FromServices] ISettingsService settingsService,
        string name,
        CancellationToken cancellationToken)
    {
        var group = await settingsService.GetSystemGroupAsync(name, cancellationToken).ConfigureAwait(false);

        return group is null ? Results.NotFound() : Results.Ok(ToResponse(group));
    }

    [MagpieCommand("patch-system", "PATCH", "groups/system/{name}", "Update a system settings group")]
    internal async Task<IResult> PatchSystemSettingsGroupAsync(
        [FromServices] ISettingsService settingsService,
        AuthenticatedUser user,
        string name,
        [FromBody] SettingsGroupPatch patch,
        CancellationToken cancellationToken)
    {
        var updated = await settingsService.PatchSystemGroupAsync(name, patch, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return updated ? Results.NoContent() : Results.NotFound();
    }

    [MagpieCommand("get", "GET", "groups/{extensionId:guid}/{name}", "Show a settings group")]
    internal async Task<IResult> GetExtensionSettingsGroupAsync(
        [FromServices] ISettingsService settingsService,
        Guid extensionId,
        string name,
        CancellationToken cancellationToken)
    {
        var group = await settingsService.GetExtensionGroupAsync(extensionId, name, cancellationToken)
            .ConfigureAwait(false);

        return group is null ? Results.NotFound() : Results.Ok(ToResponse(group));
    }

    [MagpieCommand("patch", "PATCH", "groups/{extensionId:guid}/{name}", "Update a settings group")]
    internal async Task<IResult> PatchExtensionSettingsGroupAsync(
        [FromServices] ISettingsService settingsService,
        AuthenticatedUser user,
        Guid extensionId,
        string name,
        [FromBody] SettingsGroupPatch patch,
        CancellationToken cancellationToken)
    {
        var updated = await settingsService
            .PatchExtensionGroupAsync(extensionId, name, patch, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return updated ? Results.NoContent() : Results.NotFound();
    }

    [MagpieCommand("test", "POST", "groups/{extensionId:guid}/{name}/test", "Test a settings group's connection")]
    internal async Task<IResult> TestSettingsGroupAsync(
        [FromServices] ISettingsService settingsService,
        Guid extensionId,
        string name,
        [FromBody] IntegrationSettingsTestRequest request,
        CancellationToken cancellationToken)
    {
        var result = await settingsService
            .TestGroupAsync(
                extensionId,
                name,
                request.Settings ?? new Dictionary<string, string>(),
                cancellationToken)
            .ConfigureAwait(false);

        return result is null ? Results.NotFound() : Results.Ok(result);
    }
}
