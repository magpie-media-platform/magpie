using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Data;
using Magpie.Models;
using Magpie.Models.Configuration;
using Magpie.Models.Result;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using static Magpie.Abstractions.Data.ISettingsRepository;

using HttpResult = Microsoft.AspNetCore.Http.IResult;

namespace Magpie.Api.EndpointGroups;

internal sealed class SettingsEndpointGroup(ISettingsRepository settingsRepository) : IApiEndpointGroup
{
    public string Name => "settings";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/groups", GetAllSettingsGroups)
            .WithName("GetAllSettingsGroups")
            .WithTags("Settings")
            .Produces<SettingsGroup[]>()
            .RequireAuthorization();

        builder.MapGet("/groups/{extensionId:guid}/{name}", GetSettingsGroupByKey)
            .WithName("GetSettingsGroupByKey")
            .WithTags("Settings")
            .Produces<SettingsGroup>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/groups/system/{name}", GetSystemSettingsGroupByName)
            .WithName("GetSystemSettingsGroupByName")
            .WithTags("Settings")
            .Produces<SettingsGroup>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapPost("/groups", CreateSettingsGroup)
            .WithName("CreateSettingsGroup")
            .WithTags("Settings")
            .Produces<SettingsGroup>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapDelete("/groups/{extensionId:guid}/{name}", DeleteSettingsGroup)
            .WithName("DeleteSettingsGroup")
            .WithTags("Settings")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapDelete("/groups/system/{name}", DeleteSystemSettingsGroup)
            .WithName("DeleteSystemSettingsGroup")
            .WithTags("Settings")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/{extensionId:guid}/{groupName}", GetSettings)
            .WithName("GetSettings")
            .WithTags("Settings")
            .Produces<Setting[]>()
            .RequireAuthorization();

        builder.MapGet("/system/{groupName}", GetSystemSettings)
            .WithName("GetSystemSettings")
            .WithTags("Settings")
            .Produces<Setting[]>()
            .RequireAuthorization();

        builder.MapGet("/{extensionId:guid}/{groupName}/{key}", GetSetting)
            .WithName("GetSetting")
            .WithTags("Settings")
            .Produces<Setting>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/system/{groupName}/{key}", GetSystemSetting)
            .WithName("GetSystemSetting")
            .WithTags("Settings")
            .Produces<Setting>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapDelete("/{extensionId:guid}/{groupName}/{key}", DeleteSetting)
            .WithName("DeleteSetting")
            .WithTags("Settings")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapDelete("/system/{groupName}/{key}", DeleteSystemSetting)
            .WithName("DeleteSystemSetting")
            .WithTags("Settings")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private async Task<HttpResult> GetAllSettingsGroups(CancellationToken cancellationToken)
    {
        var result = await settingsRepository.GetAllSettingsGroupsAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<SettingsGroup>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<SettingsGroup>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetSettingsGroupByKey(
        Guid extensionId,
        string name,
        CancellationToken cancellationToken)
    {
        var result = await settingsRepository.GetSettingsGroupAsync(extensionId, name, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<SettingsGroup?> { Value: null } => Results.NotFound(),
            SuccessResult<SettingsGroup?> success => Results.Ok(success.Value),
            FailureResult<SettingsGroup?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetSystemSettingsGroupByName(
        string name,
        CancellationToken cancellationToken)
    {
        var result = await settingsRepository.GetSettingsGroupAsync(null, name, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<SettingsGroup?> { Value: null } => Results.NotFound(),
            SuccessResult<SettingsGroup?> success => Results.Ok(success.Value),
            FailureResult<SettingsGroup?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> CreateSettingsGroup(
        SettingsGroup request,
        CancellationToken cancellationToken)
    {
        var settingsGroup = request with { Id = Guid.NewGuid() };

        var result = await settingsRepository.CreateAsync(settingsGroup, createdByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<SettingsGroup> success =>
                Results.Created(
                    success.Value.ExtensionId.HasValue
                        ? $"/api/settings/groups/{success.Value.ExtensionId}/{success.Value.Name}"
                        : $"/api/settings/groups/system/{success.Value.Name}",
                    success.Value),
            FailureResult<SettingsGroup> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> DeleteSettingsGroup(
        Guid extensionId,
        string name,
        CancellationToken cancellationToken)
    {
        var key = new SettingsGroupKey(extensionId, name);
        var result = await settingsRepository.DeleteAsync(key, deletedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> { Value: false } => Results.NotFound(),
            SuccessResult<bool> => Results.NoContent(),
            FailureResult<bool> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> DeleteSystemSettingsGroup(
        string name,
        CancellationToken cancellationToken)
    {
        var key = new SettingsGroupKey(null, name);
        var result = await settingsRepository.DeleteAsync(key, deletedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> { Value: false } => Results.NotFound(),
            SuccessResult<bool> => Results.NoContent(),
            FailureResult<bool> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetSettings(
        Guid extensionId,
        string groupName,
        CancellationToken cancellationToken)
    {
        var result = await settingsRepository.GetSettingsAsync(extensionId, groupName, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Setting>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Setting>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetSystemSettings(
        string groupName,
        CancellationToken cancellationToken)
    {
        var result = await settingsRepository.GetSettingsAsync(null, groupName, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Setting>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Setting>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetSetting(
        Guid extensionId,
        string groupName,
        string key,
        CancellationToken cancellationToken)
    {
        var result = await settingsRepository.GetSettingAsync(extensionId, groupName, key, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<Setting?> { Value: null } => Results.NotFound(),
            SuccessResult<Setting?> success => Results.Ok(success.Value),
            FailureResult<Setting?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetSystemSetting(
        string groupName,
        string key,
        CancellationToken cancellationToken)
    {
        var result = await settingsRepository.GetSettingsAsync(null, groupName, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Setting>> success =>
                success.Value.FirstOrDefault(s => s.Key == key) is { } setting
                    ? Results.Ok(setting)
                    : Results.NotFound(),
            FailureResult<IReadOnlyList<Setting>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> DeleteSetting(
        Guid extensionId,
        string groupName,
        string key,
        CancellationToken cancellationToken)
    {
        var settingKey = new SettingKey(extensionId, groupName, key);
        var result = await settingsRepository.DeleteAsync(settingKey, deletedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> { Value: false } => Results.NotFound(),
            SuccessResult<bool> => Results.NoContent(),
            FailureResult<bool> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> DeleteSystemSetting(
        string groupName,
        string key,
        CancellationToken cancellationToken)
    {
        var patch = new SettingsGroupPatch(
            ExtensionId: null,
            Name: groupName,
            Settings: new DictionaryPatch<string, string, string>(
                Add: null,
                Remove: [key],
                Update: null));

        var result = await settingsRepository.PatchAsync(patch, modifiedByUserId: null, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<bool> => Results.NoContent(),
            FailureResult<bool> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
